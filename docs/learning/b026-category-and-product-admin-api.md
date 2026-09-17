# B026 — S26 category and product admin API: learning note

## Files changed and why

### New

- `src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationQuery.cs`, `PaginationMetadata.cs`, `PaginationSupport.cs` — Shop's own copy of IAM's pagination binding, page metadata and `Skip`/`Take` helper. Shop cannot reference IAM's Contract project (B018/S20 dependency rule), so it keeps a module-owned copy exactly the way IAM does.
- `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs` — the tenant-membership check every Shop admin endpoint calls first. It reads the caller's account from the JWT `sub` claim and confirms an active membership in the route's tenant with a **raw SQL** `COUNT(*)` over IAM's own `iam_tenant_memberships` / `iam_accounts` / `iam_tenants` tables. B029 (shipping/coupons) reuses this file as-is.
- `src/modules/shop/TenantForge.Modules.Shop/features/categories/CategoryContracts.cs` — the plain request/response records for the category admin endpoints (no Contract project yet).
- `src/modules/shop/TenantForge.Modules.Shop/features/categories/CategoriesFeature.cs` — maps the three category routes: create, list (paginated) and update.
- `src/modules/shop/TenantForge.Modules.Shop/features/products/ProductContracts.cs` — the combined product + variants + size-guide authoring request/response records.
- `src/modules/shop/TenantForge.Modules.Shop/features/products/ProductsFeature.cs` — maps the four product routes: create, list (paginated summaries with `variantCount`), fetch-by-id and update. Create and update both persist the product, its color/size variants and its size-guide table in one transaction.
- `tests/integration/TenantForge.Api.IntegrationTests/ShopCatalogAdminIntegrationTests.cs` — API-level coverage for the happy path, the combined authoring payload, wholesale replace on update, duplicate-slug rejection, wrong size-guide row count, and the 401/403 boundary.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs` — adds `ShopAdminDbFixture` + `ShopAdminIsolatedCollection` so these tests run on a dedicated database and never inflate other classes' row counts.

### Modified

- `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs` — `UseShopModuleAsync` now ends by calling a private `MapShopModule`, which invokes `MapCategoriesFeature` and `MapProductsFeature`. The host still composes Shop through the single `UseShopModuleAsync` seam.
- `src/api/TenantForge.Api/Program.cs` — updated only the now-stale comment above `UseShopModuleAsync` (the seam now maps endpoints).
- `tasks/TASKS.md` — B026 moved through the task lifecycle for this slice.

## Request flow from endpoint to response

### `POST /api/tenants/{tenantId}/shop/categories`

1. The client calls the route with a Bearer token and `{ name, slug, displayOrder }`.
2. `RequireAuthorization` authenticates the JWT first — an unauthenticated request stops here with `401`.
3. `ShopAuthorization.AuthorizeTenantAccessAsync` parses the route `tenantId` as a TSID, reads the caller's account id from the JWT `sub`, and runs the raw-SQL membership `COUNT(*)`. Any miss (not a member, inactive account, inactive tenant, malformed id) returns `403`.
4. The handler validates `name`/`slug` server-side, then checks for a duplicate slug within the tenant (case-insensitive, since slugs are normalized to lower-case).
5. It builds a `ShopCategory`, saves it, and returns `201 Created` with the `CategoryResponse` and a `Location` header. A duplicate slug instead returns `409` as a problem document.

### `POST /api/tenants/{tenantId}/shop/products`

1. Authentication (401) then the same membership check (403) run first.
2. `ValidateProductRequestAsync` checks name, slug, duplicate slug within the tenant, category membership, at-least-one variant, unique color/size pairs, and that every size-guide row has exactly one value per column.
3. Inside one transaction the handler:
   - creates the `ShopProduct` and saves it **first** (so `product.Id` is a real row for the child FKs to point at — see the deviations note below);
   - adds every `ShopProductVariant`;
   - if size-guide columns were supplied, adds the columns and, for each row, one `ShopSizeGuideCell` per column.
   - saves again and commits.
4. `LoadProductResponseAsync` re-reads the product, its variants (ordered by color then size), its size-guide columns/rows and cells, and assembles the full `ProductResponse`. The response is `201 Created`.

### `PUT .../shop/products/{productId}`

The same auth/validation, then a **wholesale replace**: within a transaction it removes the product's existing variants, size-guide columns, rows and cells (cells removed by their row ids), saves, then re-adds everything from the submitted payload and saves again. The admin form always submits the complete current state, so a diff is unnecessary. The response is the freshly re-loaded `ProductResponse`.

## Backend concepts introduced

- **Cross-module authorization without a project reference.** A module can never `ProjectReference` another module, so Shop cannot call IAM's C# membership check. The accepted seam is that `Shop:ShopDb` and `IAM:IamDb` point at the same physical database, so Shop reads IAM's tables with raw SQL — mirroring IAM's own active-account/active-tenant join, and never modeling an `iam_*` table as an EF entity.
- **A module-owned pagination copy.** Because Shop cannot reference IAM's Contract, it carries its own `PaginationQuery`/`PaginationMetadata`/`PaginationSupport`. This is a deliberate, module-local duplication until a neutral shared contract is proven by a second consumer.
- **Transactional combined authoring.** One request persists an aggregate that spans four tables (product, variants, size-guide columns/rows/cells). The writes happen inside a single `BeginTransactionAsync`/`CommitAsync` so a partial authoring payload can never leave an inconsistent product.
- **The "save parent first" FK pattern.** With a domain model that has no navigation properties, EF cannot infer the parent→child relationship for newly-added children, so the parent row must be persisted before the children reference its key.
- **Wholesale replace for a form-shaped payload.** Replace-all is simpler and correct when the client always submits the full current state.

## Important security decisions

- **401 vs 403 split.** `RequireAuthorization` answers `401` for a missing/invalid token before any handler runs; `ShopAuthorization` answers `403` for an authenticated caller who is not an active member of the route's tenant. A non-canonical `tenantId` is denied with `403`, never `400` or `500`.
- **Server-side tenant isolation.** Every query is scoped `where tenantId == access.TenantId`; the route's `tenantId` is never trusted for data access on its own — it is always paired with a verified membership. A caller who is a member of one tenant can read or write nothing in another.
- **Default deny on the membership check.** The raw-SQL `COUNT(*)` must be `> 0` (active membership + active account + active tenant) to proceed; anything else is `403`.
- **No credentials or tokens logged.** Authorization failures return bare `401`/`403` with no detail that reveals account ids, emails or tokens.
- **TSID boundary preserved.** Route ids are parsed through `TsidId.TryParse`/`TryParseNullable`; responses format through `TsidId.Format`. No backing integer is ever serialized or accepted.

## Alternatives deliberately postponed

- **Navigation-property graph fix instead of an extra `SaveChanges`.** Adding `ICollection<ShopProductVariant>` navigations to `ShopProduct` (or explicit `EntityEntry.Reference`) would let EF infer the FK in one save. That touches the B025 domain model and its mapping for a purely cosmetic single-save gain; the minimal, Spec-aligned fix is to save the parent first.
- **Per-row endpoints instead of the combined payload.** Separate `POST /variants` and `POST /sizeGuide` routes would be more REST-purist but force the boutique-owner form into N round-trips and leave the product half-authored mid-way. The combined payload matches the real form.
- **An advisory lock (IAM's `pg_advisory_xact_lock`)** around catalog mutations is not needed yet; there is no cross-request invariant to serialize (unlike IAM's role-administrative safety net). It can be added in a later slice if a real race appears.
- **A `TenantForge.Modules.Shop.Contract` project.** Deferred by S26's explicit non-goal — Shop's request/response types stay inside the module until a second module needs to reference them.

## Commands and manual steps to verify the slice

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual (local API with a seeded tenant member):

1. Start Postgres, then `dotnet run --project src/api/TenantForge.Api --urls http://0.0.0.0:5080`.
2. Sign in as the seeded platform admin (`POST /api/auth/login`), create a user (`POST /api/platform/users`), then create a tenant with that user as owner (`POST /api/platform/tenants`).
3. Sign in as that owner and capture the `accessToken` and the tenant `id`.
4. `POST /api/tenants/<tenantId>/shop/categories` with a name/slug → expect `201`.
5. `POST /api/tenants/<tenantId>/shop/products` with `variants` and `sizeGuideColumns`/`sizeGuideRows` → expect `201` with the full `ProductResponse` (2 variants, 2 columns, 2 rows, one cell per column in order).
6. `GET /api/tenants/<tenantId>/shop/products/<productId>` → identical body.
7. Repeat `POST categories` with the same slug → `409`; repeat `POST products` with the same slug → `400` (slug in `errors`).
8. Call the same routes with a token whose account has **no** membership in that tenant → `403`; with no token → `401`.

## Three review questions for the learner

1. Why does `ShopAuthorization` use a raw `COUNT(*)` over IAM's tables instead of an EF query, and what would break if `Shop:ShopDb` and `IAM:IamDb` pointed at two different databases?
2. In the product `POST`, what exactly would fail if the `SaveChangesAsync` before `AddVariantsAndSizeGuide` were removed — and why does the `PUT` handler not have the same problem?
3. The size-guide replace removes cells by their row ids before removing the rows themselves. Why must cells be removed (or the row ids captured) before the rows, given the foreign keys on `shop_size_guide_cells`?
