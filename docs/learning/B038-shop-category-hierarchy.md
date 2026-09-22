# B038 — One level of storefront subcategories

Backend learning note for slice S34. This slice adds a single optional
`ParentCategoryId` to the existing `ShopCategory`, exposes it through the
admin create/update/list contracts and the public category list (now nested),
and threads a "effective public activity" rule through every public read.

## 1. Files changed and why

**Production**
- `domain/ShopCategory.cs` — added `Tsid? ParentCategoryId`; `Create` takes an
  optional `parentTsid` (defaults to `null` = root) and `Update` now always
  takes `parentTsid` (a full-state update, so there is no ambiguous "leave
  parent alone" default).
- `infrastructure/ShopCategoryMap.cs` — mapped the nullable column, a
  self-referencing FK to `Id` with `DeleteBehavior.Restrict`, and the
  `(TenantId, ParentCategoryId, DisplayOrder)` index.
- `infrastructure/Migrations/…_AddShopCategoryHierarchy.cs` (+ Designer) and
  `ShopDbContextModelSnapshot.cs` — the generated schema change.
- `features/categories/CategoryContracts.cs` — `ParentCategoryId` on the
  create/update requests and the admin response (string, `null` for a root).
- `features/categories/CategoriesFeature.cs` — the Spec's `ValidateParentAsync`
  (verbatim), a reparent guard, a `FOR UPDATE` row lock, and a transaction
  wrapping validate-then-save for both create and update.
- `features/categories/CategoryVisibility.cs` — the shared
  "effective public activity" predicate (one rule, applied everywhere).
- `features/storefront/StorefrontCatalogFeature.cs` — nested public list,
  root-vs-child slug resolution on both product routes, and the product-detail
  gate; `CancellationToken` threaded through the new I/O.
- `features/storefront/StorefrontContracts.cs` — `StorefrontCategoryResponse`
  gains `IReadOnlyList<StorefrontCategoryResponse> Children`.
- `features/media/ProductMediaFeature.cs` — public media-bytes route now
  requires the category to be effectively active.

**Tests**
- `ShopCategoryHierarchyIntegrationTests.cs` (new) + a dedicated
  `ShopCategoryHierarchyDbFixture` — ten one-scenario facts.

**Docs**
- `docs/design/shop/http-contracts.md` (S34 section), `docs/modules/SHOP.md`,
  this note, and the agent/human knowledge files.

## 2. Request flow

**Create with a parent** — `POST /api/tenants/{tenantId}/shop/categories`:
authorize (bearer + `Shop.Catalog.Manage`) → field validation → open a
transaction → duplicate-slug check → if a `parentCategoryId` is supplied,
lock that parent row (`SELECT … FOR UPDATE`) and apply the Spec's
eligibility rule against the locked row (same tenant — the lock query
predicates `tenant_id` — active, no parent of its own) → `ShopCategory.Create`
with the parent id → `SaveChangesAsync` → commit. Any failure path returns
before commit, so nothing is half-written. (On **create** the Spec's
self-parent branch cannot fire — the child row does not exist yet — so the
create path checks the locked row directly; on **update** it runs the Spec's
`ValidateParentAsync` verbatim.)

**Re-parent (update)** — `PUT …/categories/{categoryId}`: same shape, but it
first locks the *category being modified*, then (only if the parent actually
changes) checks `AnyAsync(child => child.ParentCategoryId == categoryId)` and
returns `409` if the category already has children. The lock + guard + save run
inside one transaction, which is what closes the "a child is added between my
check and my write" race.

**Public list** — `GET /api/shop/{tenantId}/categories`: load every
effectively-active category in one query (the shared predicate compiles to a
correlated `EXISTS`), then group children under their roots in memory. This is
a deliberate choice: the hierarchy is at most two levels, so one small query
plus a `GroupBy` is clearer than a recursive CTE.

**Product by root slug** — the SQL keeps the product query tenant-scoped and
adds `categoryId == root OR (EXISTS a category where id = product.categoryId
AND parentCategoryId = root)`. A child slug collapses to `categoryId == child`.

## 3. Backend concepts introduced

- **Self-referencing foreign key.** A table FK to its own primary key.
  `DeleteBehavior.Restrict` means the database rejects deleting/re-keying a
  parent that still has children — the hierarchy is only ever changed through
  the feature code, never accidentally cascade-deleted.
- **Row lock + guarded write.** `SELECT … FOR UPDATE` via
  `db.Categories.FromSqlRaw("… FOR UPDATE", …)` pins the row for the duration
  of the transaction. The lock, the "does it have children?" check and the
  `SaveChangesAsync` all happen under it, so the rule is enforced atomically —
  not by an earlier `AnyAsync` that another request could beat.
- **A shared translatable predicate.** `CategoryVisibility.For(queryable)`
  builds `category => category.IsActive && (parent == null ||
  queryable.Any(parent => parent.Id == category.ParentCategoryId &&
  parent.IsActive))`. EF Core translates the captured `IQueryable` into a
  correlated `EXISTS`, so the *same* rule runs in the public list, both product
  routes, product detail and media bytes without any endpoint re-implementing
  it slightly differently.
- **Expression trees for dynamic `Where`.** The predicate is returned as an
  `Expression<Func<ShopCategory, bool>>` so it can be composed with
  `.Where(...)` on an `IQueryable`. (The first attempt used
  `Expression.Constant`/`Expression.Call` with hand-built `MethodInfo`s — EF
  did not translate it; capturing the queryable in a normally-written lambda
  body is the supported pattern.)

## 4. Security decisions

- **Tenant isolation first.** Every parent/child lookup predicates on
  `TenantId` before `Id`; a foreign-tenant parent is indistinguishable from a
  missing one (same `400` field message, no detail about the other tenant).
- **Default-deny public reads.** A category is public only while it *and* its
  root are active. Deactivating a root hides children on **every** public
  surface at once (list, by-slug, all-products, detail, media bytes) — there is
  no single route that still leaks them.
- **No client-supplied hierarchy.** The parent id is a TSID string like every
  other id; the server re-derives active state from the database, never from
  the request body.

## 5. Alternatives deliberately postponed

- **Recursive CTE / arbitrary-depth tree.** The product requirement is exactly
  two levels (Darno-style grouped categories); a recursive query or a path
  column would add complexity this slice does not need. Non-goal in the Spec.
- **Storing a computed `IsLeaf`/path column.** Redundant state that could
  drift; the one-level rule is enforced and derived instead.
- **EF Core `HasQueryFilter` for visibility.** A global filter would also
  hide inactive categories from the *admin* list, which must still show them.
  An explicit, per-route predicate is the correct scope.

## 6. Commands and manual verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo --filter FullyQualifiedName~ShopCategoryHierarchyIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Manual (dev): create a tenant owner, a root category, a child under it, and a
product in each; confirm `GET /api/shop/{tenantId}/categories` nests the child
under the root; confirm the root slug lists both products; deactivate the root
and confirm the public list, the by-slug products, the product detail and the
image bytes all stop serving the child while the admin list still shows it.

## 7. Three review questions

1. Why is the reparent guard wrapped in a `FOR UPDATE` row lock instead of a
   plain `AnyAsync` check? What exact interleaving would the plain check
   miss under concurrent requests?
2. `CategoryVisibility.For` captures the queryable in the lambda. Why does EF
   Core translate that into a correlated `EXISTS`, and why did the
   `Expression.Call`/`MethodInfo` version not?
3. The public category list loads all effectively-active rows and groups them
   in memory. Given the two-level limit, is that acceptable — and at what
   point (row count, or a third hierarchy level) would you reach for a
   recursive CTE instead?
