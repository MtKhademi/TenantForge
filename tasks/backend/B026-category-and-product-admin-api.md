---
id: B026
slice: S26
title: Category and product admin API
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Give an authenticated tenant member (a boutique owner/admin) tenant-scoped
endpoints to create/list/update categories, and to create/list/update one
product together with all of its color/size variants and its size-guide
table in a single authoring payload. No public/anonymous endpoint is added
in this task.

# Context

Read `tasks/slices/026-shop-catalog.md` completely, including its "Route
convention introduced in this slice" section — every route this task adds
is under `/api/tenants/{tenantId}/shop/...`, the same shape as IAM's
existing tenant-scoped routes.

Read the real IAM tenant-scoped feature this task's authentication/
authorization and route-mapping style is modeled on:
- `src/modules/iam/TenantForge.Modules.Iam/features/tenantmembers/TenantMembersFeature.cs`
  — the `{tenantId}` route-segment pattern, and how the endpoint checks
  the caller is a member of that tenant (or a platform admin) before
  doing anything, via `.RequireAuthorization()` plus reading the
  `ClaimsPrincipal`'s tenant-membership claims/a database check. Follow
  the exact same check for every endpoint in this task — Shop introduces
  no new authorization concept, it reuses IAM's tenant-membership check
  as-is.
- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs`
  — the minimal-API endpoint-mapping and validation style (a static
  `Map<Feature>` extension method on `IEndpointRouteBuilder`, inline
  request validation returning `Results.ValidationProblem`, a `Results.Created`
  for the POST). Match this style, including a `Map<Feature>Feature`
  static class per feature folder.
- `src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs`
  — reuse this exact type (it lives in `TenantForge.Modules.Iam`, not
  BuildingBlocks or a Contract project) for the category/product list
  endpoints' pagination, the same way every existing IAM list endpoint
  does. Since `TenantForge.Modules.Shop` has no reference to
  `TenantForge.Modules.Iam` (modules do not depend on each other — only on
  BuildingBlocks and their own Contract project, per the dependency rules
  B018/S20 and the `module-contract-project` Skill established), Shop
  needs its own equivalent pagination-binding helper inside
  `TenantForge.Modules.Shop/features/pagination/` — copy
  `PaginationSupport`'s shape (`PaginationQuery`/`PaginationMetadata`
  records, `TryBind`/`PageAsync`/`Parse`) into a Shop-owned file rather
  than referencing IAM's. This is a deliberate, small duplication, not an
  oversight: two modules independently owning the same small binding
  helper is exactly what `docs/building-blocks/README.md`'s "two real
  consumers" admission bar is for — Shop's pagination_helper is a
  candidate to migrate into `TenantForge.BuildingBlocks` in a future task
  once it is proven identical in both modules, not now.

This task creates `TenantForge.Modules.Shop`'s first real HTTP request/
response types (`CreateCategoryRequest`, `CategoryResponse`,
`CreateProductRequest` with nested variant/size-guide-row payloads,
`ProductResponse`, etc.) as plain records directly inside
`TenantForge.Modules.Shop` — no `TenantForge.Modules.Shop.Contract`
project exists yet (per the slice's Non-goals); do not create one in this
task.

# Scope

1. `features/categories/CategoriesFeature.cs`:
   - `POST /api/tenants/{tenantId}/shop/categories` — body: `Name`,
     `Slug`, `DisplayOrder`. Validates `Name`/`Slug` are non-empty, `Slug`
     is unique within the tenant, returns `201` with the created
     category.
   - `GET /api/tenants/{tenantId}/shop/categories` — paginated list of
     every category for the tenant (active and inactive — this is the
     admin view), ordered by `DisplayOrder`.
   - `PUT /api/tenants/{tenantId}/shop/categories/{categoryId}` — updates
     `Name`, `Slug`, `DisplayOrder`, `IsActive`.
2. `features/products/ProductsFeature.cs`:
   - `POST /api/tenants/{tenantId}/shop/products` — one authoring payload:
     `Name`, `Slug`, `Description`, `CategoryId`, `BasePrice`,
     `CompareAtPrice`, a `Variants` list (`Color`, `Size`, `Sku`,
     `StockQuantity`, `PriceOverride`), and a size-guide payload (a list
     of column names in display order, plus a list of rows each carrying
     a `SizeLabel` and one value per column, in the same order as the
     columns list). Creates the product, its variants, its
     `ShopSizeGuideColumn`/`ShopSizeGuideRow`/`ShopSizeGuideCell` rows,
     all inside one transaction. Validates `CategoryId` belongs to the
     same tenant, `Slug` is unique within the tenant, every variant's
     `Color`+`Size` pair is unique within the product, and every row's
     value list has exactly as many entries as there are columns.
   - `GET /api/tenants/{tenantId}/shop/products` — paginated list (name,
     slug, category, base price, active flag, variant count — not the
     full variant/size-guide detail, which is the single-product GET's
     job).
   - `GET /api/tenants/{tenantId}/shop/products/{productId}` — full detail
     including variants and the size guide, for the edit form to load.
   - `PUT /api/tenants/{tenantId}/shop/products/{productId}` — updates the
     product's own fields, replaces its variant list and size-guide rows
     wholesale from the submitted payload (the admin form always submits
     the complete current state, so a diff-based partial update is not
     needed — replace-in-transaction is simpler and matches how the form
     actually works).
3. Authorization: every endpoint requires the caller to be an
   authenticated member of `{tenantId}` (or a platform admin), using the
   same check `TenantMembersFeature` already performs — do not invent a
   new Shop-specific permission/policy name; if IAM's tenant-membership
   check is currently a plain "is a member of this tenant" check (not a
   named granular permission), match that same level, since S26 does not
   introduce a granular Shop permission catalog.

# Non-goals

- No public/anonymous endpoint (that is B027).
- No `TenantForge.Modules.Shop.Contract` project.
- No granular Shop-specific permission (e.g. "can manage catalog" vs. "can
  view catalog") — any tenant member who can reach the admin area can use
  every endpoint in this task, matching the coarse level IAM's own
  tenant-membership check currently provides everywhere else.
- No image upload — a product's gallery images are out of scope for this
  task and this slice (F030/F031's product-detail mock and connect tasks
  work with whatever image handling their own Spec defines; B026 carries
  no image field).

# Acceptance

- All six endpoints above exist, tenant-scoped, requiring the tenant-
  member/platform-admin authorization check.
- Creating a product with variants and a size guide in one request
  persists all rows correctly and returns them in the response; fetching
  the same product by id returns an identical shape.
- Duplicate `Slug` (category or product) within the same tenant is
  rejected with a clear validation error; the same slug in two different
  tenants is allowed (tenant isolation, matching every existing IAM
  isolation guarantee).
- A size-guide row with the wrong number of values (not matching the
  column count) is rejected with a clear validation error, not silently
  truncated/padded.
- Updating a product replaces its variants and size-guide rows correctly
  (old rows removed, new rows present) — verified by an integration test
  that updates a product to have fewer variants than it started with and
  confirms the removed variant's row is gone.
- A caller who is not a member of `{tenantId}` gets `403`; an
  unauthenticated caller gets `401`.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: category create/list/update, product
create-with-variants-and-size-guide, product fetch-by-id, product update
replacing variants, duplicate-slug rejection, and the 401/403 authorization
cases. The full existing IAM suite continues to pass unmodified.

Manual:

- As a seeded tenant member, call each endpoint with `curl`/an HTTP client
  and confirm the documented shapes and status codes.
- Confirm a non-member of the tenant gets `403` on every endpoint.

# Lifecycle

Add row `B026` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B025`, and Spec link
`tasks/backend/B026-category-and-product-admin-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
