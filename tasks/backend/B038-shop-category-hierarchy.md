# B038 — Support one level of storefront subcategories

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S34`.
- Depends on: `B037`.
- Immediate browser consumer: `F056`. Do not widen this API for an unnamed future screen.
- Visible outcome: Catalog managers create root categories and direct children; storefront navigation mirrors Darno-style grouped categories without arbitrary trees.

## Read before editing

Read `AGENTS.md`, the `B038` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/034-shop-category-hierarchy.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

category domain/map/contracts/feature, storefront contracts/query, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

No new routes. Extend existing category create/update/list and public category list contracts additively.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add nullable `ParentCategoryId` to `ShopCategory`. Configure a same-table FK with `DeleteBehavior.Restrict`, an index `(TenantId, ParentCategoryId, DisplayOrder)` and no cascade delete.
        2. Extend create/update requests with `ParentCategoryId`. A parent must exist in the same tenant, be active, and itself have no parent. Maximum depth is therefore root + direct child. Reject self-parent, descendant parent and foreign IDs with the same `parentCategoryId` validation message.
        3. A root can be deactivated while children remain stored, but effective public activity requires both child and parent active. Reparenting is allowed only when it cannot make a parent with children into a child. Run validation and mutation in a transaction.
        4. Admin list returns flat rows with `ParentCategoryId`; public list returns roots containing ordered `Children`. Existing root-only tenants serialize children as `[]`. Update every public eligibility predicate introduced by B027/B036, including public media bytes, so a child product is public only while both child and root are active.
        5. Product filtering accepts either root or child slug. A root category result includes direct-child products plus products assigned to the root; a child result includes only that child.


## Required code shape

```csharp
        public sealed record StorefrontCategoryResponse(
            string Id, string Name, string Slug, int DisplayOrder,
            IReadOnlyList<StorefrontCategoryResponse> Children);

        private static async Task<IResult?> ValidateParentAsync(
            ShopDbContext db, Tsid tenantId, Tsid categoryId, Tsid? parentId, CancellationToken ct)
        {
            if (parentId is null) return null;
            var parent = await db.Categories.SingleOrDefaultAsync(
                c => c.Id == parentId && c.TenantId == tenantId, ct);
            if (parent is null || !parent.IsActive || parent.ParentCategoryId is not null || parent.Id == categoryId)
                return Results.ValidationProblem(new() { ["parentCategoryId"] = ["Select an active root category."] });
            return null;
        }
        ```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

root/child create and edit; maximum depth; cross-tenant parent; self-parent; parent-with-children cannot become child; effective activity; public nested ordering; root filter includes child products; old flat rows migrate as roots.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F056` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Unlimited nesting, category deletion, breadcrumbs deeper than two levels, bulk reordering.

## Acceptance checklist

- [ ] The visible outcome works through `F056` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
