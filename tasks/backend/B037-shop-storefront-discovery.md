# B037 — Add storefront search, sorting and sale discovery

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S33`.
- Depends on: `B036`.
- Immediate browser consumer: `F055`. Do not widen this API for an unnamed future screen.
- Visible outcome: A guest browses all active products, searches by product name, filters by category or sale, and chooses a deterministic sort order.

## Read before editing

Read `AGENTS.md`, the `B037` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/033-shop-storefront-discovery.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

`features/storefront/StorefrontCatalogFeature.cs`, `StorefrontContracts.cs`, pagination helper if needed, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Auth | Result |
        |---|---|---|---|
        | GET | `/api/shop/{tenantId}/products?pageNumber=1&pageSize=24&q=&categorySlug=&sort=newest&saleOnly=false` | anonymous | `200 StorefrontProductListResponse` |
        Existing category-specific and detail routes remain valid.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add one all-products query. Apply `TenantId`, `Product.IsActive` and active-category predicates before every optional filter and before `CountAsync`.
        2. `q` is trimmed, maximum 100 characters and matched case-insensitively against product name using parameterized EF/Npgsql translation. Blank means no search. `categorySlug` must resolve to an active category in the same tenant or return generic `404`.
        3. Allowed `sort`: `newest` (`Id desc`, relying on the repository's time-sortable TSID identifier), `price-asc`, `price-desc`, `name`. Because effective price may vary by variant, this task defines the card price as the lowest effective price among variants with `StockQuantity > 0`; products with no in-stock variant are still listed last as sold out using `BasePrice` as display fallback.
        4. `saleOnly=true` requires `CompareAtPrice > card price`. Add `IsOnSale`, `IsSoldOut`, `ThumbnailUrl` to each summary. Do not expose SKU or exact stock count.
        5. Return current pagination metadata and validate page bounds through existing `PaginationSupport`. Stable tie-breaker is always product ID.


## Required code shape

```csharp
        public sealed record StorefrontProductSummaryResponse(
            string Id, string Name, string Slug, decimal EffectivePrice,
            decimal? CompareAtPrice, bool IsOnSale, bool IsSoldOut, string? ThumbnailUrl);

        var query = db.Products.AsNoTracking()
            .Where(p => p.TenantId == tenantTsid && p.IsActive)
            .Where(p => db.Categories.Any(c => c.Id == p.CategoryId && c.TenantId == tenantTsid && c.IsActive));
        // Apply q/category/sale, then CountAsync, then the selected stable order, Skip/Take.
        ```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

tenant isolation; inactive category/product excluded; q trim/case/max length; invalid sort 400; category from another tenant 404; every sort including equal-price tie; sale-only; sold-out ordering; thumbnail order; pagination total reflects filters.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F055` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Full-text search engine, popularity/rating sort, recommendation engine, tags or faceted color/size filters.

## Acceptance checklist

- [ ] The visible outcome works through `F055` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
