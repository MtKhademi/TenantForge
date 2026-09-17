---
id: B027
slice: S26
title: Public storefront catalog read API
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Add the first anonymous Shop endpoints: list active categories, list
active products within a category (paginated), and fetch one active
product's full detail (variants with live stock, size-guide table) by
slug — all unauthenticated, tenant-scoped by a `{tenantId}` route segment
under the new `/api/shop/{tenantId}/...` prefix.

# Context

Read `tasks/slices/026-shop-catalog.md` completely, in particular "Route
convention introduced in this slice": this task is the first to use the
`/api/shop/{tenantId}/...` anonymous prefix, distinct from B026's
authenticated `/api/tenants/{tenantId}/shop/...` admin prefix. IAM has no
precedent for an anonymous, tenant-scoped route (every existing IAM route
either requires platform-admin or an authenticated tenant member), so
there is no `.AllowAnonymous()` call anywhere yet in
`src/modules/iam/**` to copy — minimal APIs are anonymous by default
unless `.RequireAuthorization()` is called, so these endpoints simply omit
that call; do not add an explicit `.AllowAnonymous()` unless a shared
fallback policy elsewhere in the host would otherwise require it (check
`Program.cs`/`IAMConfig.RegisterServices` for any global
`RequireAuthorization` default before assuming none is needed).

Read `src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs`
again for the pagination-response shape this task's product-list endpoint
reuses (via the Shop-owned equivalent B026 already created under
`TenantForge.Modules.Shop/features/pagination/` — reuse that file, do not
create a second copy).

This task only reads the tables B025 created and the rows B026's admin
endpoints let a tenant owner populate; it defines no new table.

# Scope

`features/storefront/StorefrontCatalogFeature.cs`:

1. `GET /api/shop/{tenantId}/categories` — every `ShopCategory` for the
   tenant where `IsActive == true`, ordered by `DisplayOrder`. No
   pagination (a boutique's category count is small; do not add pagination
   metadata for a list that will realistically never need a second page —
   revisit only if a real tenant's category count later proves this
   wrong).
2. `GET /api/shop/{tenantId}/categories/{categorySlug}/products` —
   paginated list of `ShopProduct` rows where `IsActive == true` and
   `CategoryId` matches the category resolved from `categorySlug` (itself
   filtered to `IsActive == true` — an inactive category never exposes its
   products publicly even if the product rows themselves are active).
   Reuses the same pagination query-string convention (`pageNumber`,
   `pageSize`) and response metadata shape as every existing paginated IAM
   list (S15/B015).
3. `GET /api/shop/{tenantId}/products/{productSlug}` — one active
   product's full detail: its own fields, every variant (`Color`, `Size`,
   `Sku` is admin-only and excluded from this public response, live
   `StockQuantity`, effective price = `PriceOverride` if set otherwise
   `BasePrice`), and its size-guide table (columns in `DisplayOrder`, rows
   in `DisplayOrder` with each cell's value). A slug that does not resolve
   to an active product for that tenant returns `404`, not an empty body.
4. Every response type here is a new plain record inside
   `TenantForge.Modules.Shop` (still no Contract project — same reasoning
   as B026).

# Non-goals

- No admin/authenticated endpoint (B026 already has those).
- No `Sku` in the public product-detail response — it is an internal
  inventory identifier, not customer-facing.
- No inactive category/product/variant ever appears in any response from
  this task, regardless of query parameters — there is no "show inactive"
  toggle on the public API.

# Acceptance

- All three endpoints work without any `Authorization` header.
- Inactive categories/products never appear, even when directly requested
  by slug (`404` for an inactive product's slug, exactly like a
  nonexistent one — do not leak "it exists but is inactive" as a distinct
  response).
- Product detail returns live `StockQuantity` per variant (not a
  snapshot) and the effective price (`PriceOverride` when set).
- Pagination on the category-products endpoint matches the existing
  IAM pagination response shape (same field names/casing).
- Tenant isolation: a `{tenantId}` that does not match the product/
  category's actual tenant never returns that product/category (proven
  by a cross-tenant integration test, matching the isolation tests IAM's
  own suite already has for its own resources).

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: active-category listing excluding inactive
rows, paginated product listing within a category, product detail by
slug (including size-guide table shape), 404 for an inactive/nonexistent
slug, and cross-tenant isolation. The full existing IAM suite continues to
pass unmodified.

Manual:

- With no `Authorization` header, call each of the three endpoints against
  data created via B026's admin API in a prior manual step and confirm the
  documented shapes.
- Deactivate a product via B026's admin `PUT` and confirm its public
  detail endpoint now returns `404`.

# Lifecycle

Add row `B027` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B026`, and Spec link
`tasks/backend/B027-public-storefront-catalog-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
