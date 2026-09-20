# B037 — Add storefront search, sorting and sale discovery

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S33`.
- Depends on: `B036`.
- Immediate browser consumer: `F055`. Do not widen this API for an unnamed future screen.
- Visible outcome: A guest browses all active products, searches by product name, filters by category or sale, and chooses a deterministic sort order.

## Do this in order

Before step 1, follow the boilerplate already described in `AGENTS.md` and in
"Read before editing" below: read `AGENTS.md`, read
`tasks/slices/033-shop-storefront-discovery.md`, read the `B037` row in
`tasks/TASKS.md`, read `docs/modules/SHOP.md` if it exists, read the matching
section of `docs/design/shop/http-contracts.md`, follow the branch-naming and
ledger-update rules from the Ownership section, and get plan approval before
writing code (backend tasks always wait for approval after the plan).

1. Open `features/storefront/StorefrontCatalogFeature.cs` (or the module's
   current equivalent) and locate the existing product-listing endpoint
   registration.
2. Add one new query handler for `GET /api/shop/{tenantId}/products` with the
   query-string parameters shown in "HTTP contract" below
   (`pageNumber`, `pageSize`, `q`, `categorySlug`, `sort`, `saleOnly`).
3. Build the base EF query exactly as shown in "Required code shape": filter
   by `TenantId`, `IsActive`, and an active-category subquery, in that order,
   before any optional filter and before calling `CountAsync`.
4. Implement the `q` filter: trim the value, reject/truncate at 100
   characters maximum, and match case-insensitively against product name
   using parameterized EF/Npgsql translation (this just means: let EF Core
   translate a normal `string.Contains`/`ILike`-style comparison into a
   parameterized SQL query — never build SQL by string concatenation). A
   blank `q` means no search filter is applied.
5. Implement the `categorySlug` filter: resolve it to an active category in
   the same tenant; if it does not resolve, return the generic non-leaking
   `404` used elsewhere in the module.
6. Implement card pricing: for each product, the "card price" is the lowest
   `EffectivePrice` among its variants where `StockQuantity > 0`. If no
   variant is in stock, the product is `IsSoldOut = true` and its displayed
   price falls back to `BasePrice`.
7. Implement `sort` with exactly these four allowed values, each with a
   stable tie-breaker of product `Id`:
   - `newest` — order by `Id desc` (this works because the module's TSID
     identifiers are time-sortable — a higher TSID was created later).
   - `price-asc` — order by card price ascending.
   - `price-desc` — order by card price descending.
   - `name` — order by product name ascending.
   Any other value must return `400`.
8. Implement `saleOnly=true`: include only products where
   `CompareAtPrice > card price`. Add `IsOnSale`, `IsSoldOut`, `ThumbnailUrl`
   to `StorefrontProductSummaryResponse`. Do not add or expose `Sku` or an
   exact stock-count field anywhere in this response.
9. Apply pagination using the existing `PaginationSupport` helper: validate
   page bounds the same way other paginated endpoints in this module do,
   call `CountAsync` after all filters (but before `Skip`/`Take`), and keep
   product `Id` as the final tie-breaker for every sort so equal-price/equal
   name rows are still deterministically ordered.
10. Add or extend integration tests in the closest existing
    `Shop*IntegrationTests.cs` file (or a new file named after this feature)
    covering every scenario in "Integration tests required" below — one test
    method per scenario.
11. Update the matching section of `docs/design/shop/http-contracts.md` to
    match exactly what you delivered.
12. Update `docs/modules/SHOP.md` (or state
    `SHOP.md impact: none — <reason>` if nothing documented changed) and
    write the `B037` learning note at `docs/learning/B037-<slug>.md`.
13. Run every command in "Validation" below, in order, and fix failures
    before moving on.
14. Follow the "Browser handoff" section to hand `F055` what it needs.
15. Work through every line of "Acceptance checklist" below and check it off
    only once you have evidence for it.

## Read before editing

Read `AGENTS.md`, the `B037` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/033-shop-storefront-discovery.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs (TSID = "a sortable numeric string ID, see `TenantForge.BuildingBlocks`" — higher TSID values were created later, which is what makes `newest` sort by `Id desc` work), a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

`features/storefront/StorefrontCatalogFeature.cs`, `StorefrontContracts.cs`, pagination helper if needed, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Auth | Result |
        |---|---|---|---|
        | GET | `/api/shop/{tenantId}/products?pageNumber=1&pageSize=24&q=&categorySlug=&sort=newest&saleOnly=false` | anonymous | `200 StorefrontProductListResponse` |
        Existing category-specific and detail routes remain valid.

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = "the repo's standard JSON error body shape for HTTP errors — reuse the existing helper, don't invent a new error format"). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

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

The snippets define names, ownership and invariants. Complete the omitted
mapping/validation/async code; do not paste placeholder comments into
production. Keep feature types `internal` except HTTP records already
following the module's current public-record convention.

Members you must still add yourself, one item per bullet, with exactly this
name/type/behavior — none of this is a free judgment call, follow it exactly:

- After the base `query` shown above, apply, in this exact order: (a) the
  `q` filter (only if `q` is non-blank after trimming and truncating to 100
  chars), (b) the `categorySlug` filter (resolved to a category id first,
  `404` if it does not resolve), (c) the `saleOnly` filter (only if
  `saleOnly=true`). Then call `CountAsync` for the total. Then apply the
  selected `sort`'s `OrderBy`/`ThenBy Id` (or `400` if `sort` is not one of
  the four allowed values). Then apply `Skip`/`Take` from `PaginationSupport`.
- A helper (name it `ResolveCardPriceAsync` or similar, internal, static or
  instance method matching the module's existing style) that computes, per
  product, the minimum `EffectivePrice` among variants with
  `StockQuantity > 0`, or `null` if none are in stock. Use this helper's
  result both for `EffectivePrice`/`IsSoldOut` on the response and for
  `price-asc`/`price-desc` sort and for `saleOnly` comparison against
  `CompareAtPrice`. When sold out, `EffectivePrice` in the response falls
  back to `BasePrice`.
- `StorefrontProductListResponse` (not shown in the snippet — add it,
  following the module's existing paginated-list response shape): a record
  wrapping `IReadOnlyList<StorefrontProductSummaryResponse>` plus the
  existing pagination metadata fields (page number, page size, total count)
  already used by other paginated Shop responses.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O ("abort-aware": if the caller disconnects or the request times out, in-flight database work should observe the token and stop rather than run to completion unobserved).
- Use `TimeProvider` where this task adds time-dependent behavior (the repo's injectable clock abstraction — use it instead of `DateTime.UtcNow` directly so tests can control time).
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each is phrased as "do X, then assert Y":

- [ ] Write a test that requests products across two tenants, then asserts each tenant only sees its own products (tenant isolation).
- [ ] Write a test that includes an inactive category or inactive product in the data, then asserts it is excluded from results.
- [ ] Write a test with `q` containing leading/trailing whitespace, mixed case, and a value over 100 characters, then asserts trimming, case-insensitive matching, and the max-length rule all behave correctly.
- [ ] Write a test with an invalid `sort` value, then asserts a `400` response.
- [ ] Write a test with a `categorySlug` belonging to another tenant, then asserts a generic `404`.
- [ ] Write a test for every sort value (`newest`, `price-asc`, `price-desc`, `name`), including a case with two products at an equal price, then asserts the order is correct and the equal-price tie is broken deterministically by product ID.
- [ ] Write a test with `saleOnly=true`, then asserts only products where `CompareAtPrice` is greater than the card price are returned.
- [ ] Write a test with a product that has no in-stock variant, then asserts it is marked sold out and ordered after in-stock products.
- [ ] Write a test asserting each summary's `ThumbnailUrl` matches the first ordered image (per B036) and that ordering of images doesn't affect product ordering.
- [ ] Write a test that applies pagination together with a filter, then asserts the total count reflects the filtered set, not the whole catalog.

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
