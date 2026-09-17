# B027 — S26 public storefront catalog read API: learning note

## Files changed and why

### New

- `src/modules/shop/TenantForge.Modules.Shop/features/storefront/StorefrontContracts.cs` — the nine anonymous-facing response records (category list, paginated product summaries, product detail with live variants and the size-guide table). These are the public storefront HTTP shapes; the admin shapes from B026 live in their own feature folders and are intentionally different (e.g. no `Sku`, no `tenantId`, an `effectivePrice` computed on the wire).
- `src/modules/shop/TenantForge.Modules.Shop/features/storefront/StorefrontCatalogFeature.cs` — the three `GET` routes under the new `/api/shop/{tenantId}/...` anonymous prefix. This is the first feature in the module with **no** `.RequireAuthorization()` and **no** `ClaimsPrincipal` parameter.
- `tests/integration/TenantForge.Api.IntegrationTests/ShopStorefrontCatalogIntegrationTests.cs` — six facts that author data through B026's *authenticated* admin API and read it back with a bare client (no `Authorization` header), proving anonymity, active-only filtering, pagination, live stock/effective price, the size-guide shape, inactive→404, cross-tenant isolation, and the 404-for-malformed-tenantId behavior.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs` — adds `ShopStorefrontDbFixture` + `ShopStorefrontIsolatedCollection` so these tests run on a dedicated database (`tenantforge_shop_storefront_tests`) and never inflate the B026 admin DB's row counts.

### Modified

- `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs` — `MapShopModule` now also calls `endpoints.MapStorefrontCatalogFeature()`, with the matching `using`. The host still composes Shop through the single `UseShopModuleAsync` seam; no host (`Program.cs`) change was needed because the seam already maps every Shop endpoint.
- `tasks/TASKS.md` — B027 moved through the task lifecycle for this slice.

## Request flow from endpoint to response

### `GET /api/shop/{tenantId}/categories`

1. The client calls the route with **no** credentials. There is no `.RequireAuthorization()`, so the authentication/authorization middleware lets an anonymous request through (there is no host-level `DefaultPolicy` or `FallbackPolicy` to catch it — IAM only registers a named `PlatformAdmin` policy).
2. The handler's first line is `TsidId.TryParse(tenantId, ...)`. A route segment that is not a canonical 13-character TSID returns `404 NotFound` — never `400` or `500` — so a malformed tenant id looks exactly like "no such tenant."
3. The query is scoped `category.TenantId == tenantTsid && category.IsActive`, ordered by `DisplayOrder` then `Id`, and projected directly into `StorefrontCategoryResponse` (id formatted through `TsidId.Format`). `200 OK`.

### `GET /api/shop/{tenantId}/categories/{categorySlug}/products`

1. Same anonymous entry and TSID guard.
2. `PaginationSupport.TryBind` reads `pageNumber`/`pageSize` (defaults 1/50, max page size 100); an invalid value returns `400` as a validation problem, exactly like the admin lists.
3. The category is looked up by normalized lower-case slug, scoped to the route tenant and `IsActive`; a miss (unknown or inactive category) is `404`.
4. Products are filtered to that category + tenant + `IsActive`, ordered by `Name` then `Id`, and paged through the shared Shop `PaginationSupport.PageAsync`. Each summary's `effectivePrice` is the product `BasePrice` (a variant's `PriceOverride` is a *detail-page* concern, not a card concern); `compareAtPrice` passes through. `200 OK` with the same `pagination` metadata shape as B026.

### `GET /api/shop/{tenantId}/products/{productSlug}`

1. Anonymous entry, TSID guard, then a normalized-slug lookup scoped to the route tenant and `IsActive` — an inactive product is indistinguishable from a nonexistent one (`404`).
2. Variants are loaded ordered by `Color` then `Size`. Each `StorefrontVariantResponse` carries the **live** `StockQuantity` straight from `ShopProductVariant` and an `EffectivePrice` of `PriceOverride ?? BasePrice`.
3. The size-guide table is reassembled from its real row/column/cell tables (same four-table join B026's admin loader uses): columns by `DisplayOrder`, rows by `DisplayOrder`, and one `StorefrontSizeGuideCellResponse` per (row, column) — an empty cell becomes `""`. The response exposes `categoryId` but never the tenant id, and it contains no `sku` field at all.

## Backend concepts introduced

- **Minimal APIs are anonymous by default.** The security decision here is an *omission*: not calling `.RequireAuthorization()` is what makes the route anonymous. This only works because the host has no default authorization policy (IAM registers just one *named* policy). If someone later added a `DefaultPolicy` or `FallbackPolicy`, these routes would silently start returning 401 — a trap the S26 slice names explicitly.
- **Tenant isolation without authentication.** B026 paired every query with a verified membership (`ShopAuthorization`); B027 has no principal to check. Isolation is enforced purely by scoping every query to the route's `{tenantId}`. The segment is *trusted* the same way IAM's own tenant-scoped routes trust it — the storefront has no separate public slug or subdomain concept, per S26.
- **A read-side projection distinct from the write-side shape.** The same `ShopProduct` row yields two different DTOs: the admin `ProductResponse` (with `sku`, `tenantId`, `isActive`, raw `priceOverride`) and the storefront `StorefrontProductDetailResponse` (with computed `effectivePrice`, no `sku`, no `tenantId`). Choosing what a public endpoint exposes is a deliberate, per-endpoint decision.
- **A clean 404 for a malformed route id.** Rather than distinguishing "invalid TSID" from "no such tenant" (which would leak that a tenant with that id format is expected), the handler returns the same `404` for both — the storefront never reveals why a segment failed.
- **A Shop-owned pagination copy reused.** The category-products endpoint reuses B026's module-local `PaginationSupport` rather than introducing anything new, keeping the S15/B015 response metadata shape identical across the module.

## Important security decisions

- **Deliberately anonymous, guest-first.** Every Shop endpoint is anonymous by design (S26/S27 non-goal: no customer accounts). These three read endpoints expose only *active* catalog data — stock quantities and prices, never PII, never credentials, never a raw `bigint` id.
- **Active-only, server-side.** `IsActive` is filtered in the database query, not in the UI. An inactive product or category returns `404` even when requested directly by slug, so deactivating a listing immediately removes it from the public surface.
- **No secrets, no tokens, no principal.** Nothing here reads or logs a token; the endpoints take no `ClaimsPrincipal`. The only identifiers crossing the wire are canonical TSID strings.
- **Default-deny on the tenant segment.** A non-canonical `tenantId` is rejected as `404` before any query runs; there is no code path where an unparsed or default TSID reaches the database.

## Alternatives deliberately postponed

- **A `.AllowAnonymous()` on each route.** Redundant — minimal APIs are already anonymous, and calling `.AllowAnonymous()` would falsely imply a default policy exists that must be opted out of. The Spec requires neither.
- **A `TenantForge.Modules.Shop.Contract` project.** Still deferred by S26's explicit non-goal: Shop's request/response types stay module-local until a second module needs to reference them.
- **A public category/product slug in place of the tenant TSID segment.** S26 intentionally reuses the same public tenant identifier everywhere; introducing a separate storefront identity would be speculative until a real consumer needs multi-tenant subdomains.
- **Caching the catalog reads.** The storefront reads are cheap, single-tenant, `AsNoTracking` queries; a cache layer is a later, consumer-driven optimization, not part of this slice.
- **A shared size-guide projection helper between admin and storefront.** B026's admin loader and this storefront loader overlap a few lines; extracting a helper now would be premature — the two projections already diverge (admin returns `sku`/`tenantId`/`isActive` and raw `priceOverride`; storefront returns computed `effectivePrice` and omits those).

## Commands and manual steps to verify the slice

Automated:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual (local API + Postgres, as in the live demo):

1. Start Postgres, then `dotnet run --project src/api/TenantForge.Api --urls http://0.0.0.0:5080` (from WSL, curl through the WSL gateway IP).
2. As the seeded platform admin: create an owner user (`POST /api/platform/users`), create a tenant with that user as owner (`POST /api/platform/tenants`), and sign in as that owner (`POST /api/auth/login`).
3. As the owner, `POST /api/tenants/<tenantId>/shop/categories` and `POST /api/tenants/<tenantId>/shop/products` (with `variants` + `sizeGuideColumns`/`sizeGuideRows`) to author the catalog.
4. With **no** `Authorization` header:
   - `GET /api/shop/<tenantId>/categories` → `200` with the active categories.
   - `GET /api/shop/<tenantId>/categories/<categorySlug>/products?pageNumber=1&pageSize=5` → `200` with `products` + `pagination`.
   - `GET /api/shop/<tenantId>/products/<productSlug>` → `200` with live `stockQuantity`, `effectivePrice`, the size-guide table, and **no** `sku`.
5. `PUT` the product with `isActive:false` (admin), then repeat step 4's product-detail call → `404`.
6. `GET /api/shop/not-a-tsid/categories` → `404`.

## Three review questions for the learner

1. B026 enforced tenant isolation by pairing every query with a verified membership. B027 has no principal at all. *Where exactly* is tenant isolation now enforced, and what would a caller be able to do if the `product.TenantId == tenantTsid` predicate were accidentally dropped from the detail query?
2. Why does a non-canonical `tenantId` return `404` instead of `400`? What information would a `400` leak to an anonymous caller, and why is that a problem for a public storefront?
3. The storefront detail computes `EffectivePrice = PriceOverride ?? BasePrice` on the wire, while the admin response returns the raw `PriceOverride`. Why is that a sensible split, and what would break on the frontend if the admin endpoint also returned a computed `effectivePrice`?
