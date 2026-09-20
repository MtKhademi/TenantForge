# B038 — Support one level of storefront subcategories

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S34`.
- Depends on: `B037`.
- Immediate browser consumer: `F056`. Do not widen this API for an unnamed future screen.
- Visible outcome: Catalog managers create root categories and direct children; storefront navigation mirrors Darno-style grouped categories without arbitrary trees.

## Do this in order

Before step 1, follow the boilerplate already described in `AGENTS.md` and in
"Read before editing" below: read `AGENTS.md`, read
`tasks/slices/034-shop-category-hierarchy.md`, read the `B038` row in
`tasks/TASKS.md`, read `docs/modules/SHOP.md` if it exists, read the matching
section of `docs/design/shop/http-contracts.md`, follow the branch-naming and
ledger-update rules from the Ownership section, and get plan approval before
writing code (backend tasks always wait for approval after the plan).

1. Add a nullable `ParentCategoryId` property (type `Tsid?`) to the existing
   `ShopCategory` entity.
2. In the category's EF Core map/configuration, add: a self-referencing
   (same-table) foreign key from `ParentCategoryId` to `ShopCategory.Id` with
   `DeleteBehavior.Restrict` (this means: deleting/changing a parent row is
   blocked by the database if children still reference it — no cascade
   delete), and an index on `(TenantId, ParentCategoryId, DisplayOrder)`.
3. Create the helper `ValidateParentAsync` exactly as shown in "Required code
   shape" below, in the same file as the category create/update feature
   code. Call it from both the create endpoint and the update endpoint
   before saving.
4. Extend the category create request and the category update request
   contracts with a nullable `ParentCategoryId` field.
5. Implement the parent validation rule (this is what `ValidateParentAsync`
   encodes): a supplied `ParentCategoryId` must (a) exist in the same
   tenant, (b) belong to an active category, (c) itself have no parent (so
   maximum depth is root + one direct child level), and (d) not equal the
   category's own ID (no self-parent). Any violation — self-parent,
   descendant-as-parent, or a foreign/other-tenant ID — returns the same
   validation error message on the `parentCategoryId` field, shown in
   "Required code shape".
6. Implement reparenting rules: a root category can be deactivated while its
   children remain stored (children are simply not publicly visible — see
   step 7). Reparenting an existing category is only allowed when the
   result would not turn a parent that currently has children into someone
   else's child. Run all of this validation plus the actual save inside one
   transaction.
7. Implement "effective public activity": a child category or its products
   are publicly visible only when both the child and its root parent are
   `IsActive`. Update every public eligibility predicate introduced by
   B027/B036 (including the public media-bytes route from B036) to also
   check the parent's active state, not just the child's.
8. Update the admin category list response to include `ParentCategoryId` on
   each flat row (no shape change needed beyond adding this field).
9. Update the public category list response to nest: return only root
   categories, each with an ordered `Children` list of
   `StorefrontCategoryResponse` (shown in "Required code shape"). A
   root category with no children still returns `Children: []` (this keeps
   existing single-level tenants working with no visible change).
10. Update product filtering by category slug: resolving a root category's
    slug must include products assigned directly to the root plus products
    assigned to any of its direct children. Resolving a child category's
    slug must include only that child's own products.
11. Write an EF Core migration ("EF migration" = running
    `dotnet ef migrations add <Name>` inside the Shop module project) that
    adds the `ParentCategoryId` column, its foreign key and its index. Every
    existing category row must migrate with `ParentCategoryId = NULL`,
    i.e. as a root category — do not change existing IDs or slugs.
12. Add or extend integration tests in the closest existing
    `Shop*IntegrationTests.cs` file (or a new file named after this feature)
    covering every scenario in "Integration tests required" below — one test
    method per scenario.
13. Update the matching section of `docs/design/shop/http-contracts.md` to
    match exactly what you delivered.
14. Update `docs/modules/SHOP.md` (or state
    `SHOP.md impact: none — <reason>` if nothing documented changed) and
    write the `B038` learning note at `docs/learning/B038-<slug>.md`.
15. Run every command in "Validation" below, in order, and fix failures
    before moving on.
16. Follow the "Browser handoff" section to hand `F056` what it needs.
17. Work through every line of "Acceptance checklist" below and check it off
    only once you have evidence for it.

## Read before editing

Read `AGENTS.md`, the `B038` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/034-shop-category-hierarchy.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Baseline as of commit `34dc44e` (later commits changed only `src/web/**` and
`tasks/**`, so this still describes the backend you will find): Shop uses one module project, internal EF entities, TSID IDs ("a sortable numeric string ID, see `TenantForge.BuildingBlocks`" — this is the type of `Id`/`ParentCategoryId`/`tenantId`/`categoryId` below), a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

category domain/map/contracts/feature, storefront contracts/query, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

No new routes. Extend existing category create/update/list and public category list contracts additively.

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = "the repo's standard JSON error body shape for HTTP errors — reuse the existing helper, don't invent a new error format"). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

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

The snippets define names, ownership and invariants. Complete the omitted
mapping/validation/async code; do not paste placeholder comments into
production. Keep feature types `internal` except HTTP records already
following the module's current public-record convention. `ValidateParentAsync`
and `StorefrontCategoryResponse` above are already complete — use them
verbatim; do not rewrite their logic or field list.

Members you must still add yourself, one item per bullet, with exactly this
name/type/behavior:

- A reparenting guard (call it from the update endpoint before saving,
  alongside `ValidateParentAsync`): before allowing a category's
  `ParentCategoryId` to change, check whether that category currently has
  any children (`db.Categories.AnyAsync(c => c.ParentCategoryId == categoryId, ct)`);
  if it does, reject the reparent with the same kind of validation error,
  because a parent-with-children can never become someone else's child.
- A public-eligibility helper/predicate (or inline `Where` clause reused
  everywhere public eligibility is checked, including the B036 public
  media-bytes route) that resolves to true only when: the category itself is
  `IsActive`, and either it has no `ParentCategoryId` or its parent is also
  `IsActive`. Apply this same predicate consistently instead of duplicating
  slightly different active checks in different endpoints.
- The product-by-category-slug resolution: given a slug, look up the
  category; if it has no `ParentCategoryId` (it's a root), match products
  whose `CategoryId` is that root's ID OR whose category's
  `ParentCategoryId` equals that root's ID; if it has a `ParentCategoryId`
  (it's a child), match only products whose `CategoryId` equals that child's
  own ID.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O ("abort-aware": if the caller disconnects or the request times out, in-flight database work should observe the token and stop rather than run to completion unobserved).
- Use `TimeProvider` where this task adds time-dependent behavior (the repo's injectable clock abstraction — use it instead of `DateTime.UtcNow` directly so tests can control time).
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each is phrased as "do X, then assert Y":

- [ ] Write a test that creates a root category and a direct child category, and edits each, then asserts both operations succeed and persist correctly.
- [ ] Write a test that attempts to create a category whose parent already has a parent (i.e. a grandchild), then asserts it is rejected for exceeding maximum depth.
- [ ] Write a test that supplies a `ParentCategoryId` belonging to another tenant, then asserts it is rejected.
- [ ] Write a test that supplies a category's own ID as its `ParentCategoryId`, then asserts it is rejected (self-parent).
- [ ] Write a test that attempts to reparent a category that currently has children, then asserts it is rejected because a parent-with-children cannot become a child.
- [ ] Write a test that deactivates a root category while its child remains active, then asserts the child is no longer publicly visible (effective activity requires both active).
- [ ] Write a test that requests the public category list, then asserts roots are returned with their children nested in the correct display order.
- [ ] Write a test that filters products by a root category's slug, then asserts it returns products assigned to the root plus products assigned to its direct children.
- [ ] Write a test that runs against pre-B038 flat category rows, then asserts they migrate as roots (`ParentCategoryId = NULL`) with unchanged IDs and slugs.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F056` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

Run these from the repository root, in this order, and fix every failure
before moving to the next command.

On the reference WSL setup there is no Linux `dotnet` binary — use `dotnet.exe`
instead of `dotnet` in every command below. See
`docs/architecture.md#local-development-environment-wsl--windows-net-sdk`.

The integration tests start PostgreSQL through Testcontainers, so Docker must
be running before you run any test command. Start it with `docker compose up -d postgres`
if Docker Desktop is not already up (the compose service is not what the tests
connect to, but it confirms the Docker daemon is reachable).

1. Build everything:

   ```bash
   dotnet build TenantForge.sln --nologo
   ```

2. Run only this task's Shop integration tests first (replace
   `<ShopTestClass>` with the exact class name you added or extended, for
   example `ShopCatalogAdminIntegrationTests`):

   ```bash
   dotnet test TenantForge.sln --nologo --filter FullyQualifiedName~<ShopTestClass>
   ```

3. Run the full test suite and confirm it is green:

   ```bash
   dotnet test TenantForge.sln --nologo
   ```

4. Open the migration file you generated under
   `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
   read it line by line. Confirm it contains only the schema changes this Spec
   asked for and nothing else. Confirm `ShopDbContextModelSnapshot.cs` was
   updated in the same change.

5. Re-read `docs/modules/SHOP.md` and check every routes/entities/config/auth/tests
   statement against the code you actually delivered. Then finish the
   `docs/learning/B038-<slug>.md` learning note.

6. Confirm the frontend was not touched:

   ```bash
   git diff --name-only origin/main... -- src/web
   ```

   This must print nothing.

## Non-goals

Unlimited nesting, category deletion, breadcrumbs deeper than two levels, bulk reordering.

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   grouped as: production code, EF migration (generated), tests,
   documentation. Say which files are generated rather than hand-written.
2. **Implementation decisions.** Every decision this Spec left to you, with
   the option you picked and one sentence of why. If you followed an "if
   unsure, do X" default from this Spec, say so and name it.
3. **Commands executed.** Every command from "Validation" above, copied
   verbatim in the order you ran them.
4. **Results of those checks.** For each command: pass or fail, and for the
   test commands the actual passed/failed/skipped counts. If you had to re-run
   something after a fix, say that and give the final result. Never report a
   command as passing if you did not run it.
5. **Risks, blockers and follow-up.** Anything you could not verify, any
   scenario from "Integration tests required" you could not cover and why, any
   contract detail that differed from this Spec, and anything the next task
   (F056) must know. Write "None." if there is genuinely nothing.
6. **Documentation impact statement.** The exact line
   `SHOP.md impact: <what you updated>` or
   `SHOP.md impact: none — <specific reason>`, plus the same line for
   `IAM.md`, `BuildingBlocks docs` and `IAM Contract docs` if your diff touched
   any of them (see `AGENTS.md`). A vague "docs not needed" is not accepted.

## Acceptance checklist

- [ ] The visible outcome works through `F056` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
