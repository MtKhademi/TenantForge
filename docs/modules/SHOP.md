# Shop module handbook

**Read this first for Shop questions.** This document describes current,
merged Shop behavior only. It is not a task diary: it contains no
`planned`/task-status language and no historical narrative. When it disagrees
with code, migrations or tests, the code is the source of truth — report the
mismatch and verify before trusting either.

## Table of contents

1. [Purpose and non-goals](#1-purpose-and-non-goals)
2. [Fast facts](#2-fast-facts)
3. [Dependency and composition boundary](#3-dependency-and-composition-boundary)
4. [Source-code map](#4-source-code-map)
5. [Configuration](#5-configuration)
6. [Domain model and invariants](#6-domain-model-and-invariants)
7. [Persistence](#7-persistence)
8. [Tenant/auth rules](#8-tenantauth-rules)
9. [Endpoint catalog](#9-endpoint-catalog)
10. [Product media](#10-product-media)
11. [Inventory reservation](#11-inventory-reservation)
12. [Coupon rules](#12-coupon-rules)
13. [Sandbox payment](#13-sandbox-payment)
14. [Storefront profile and policies](#14-storefront-profile-and-policies)
15. [Test map and commands](#15-test-map-and-commands)
16. [Current limitations](#16-current-limitations)
17. [Change-impact checklist](#17-change-impact-checklist)

## 1. Purpose and non-goals

Shop owns:

- tenant-scoped catalog authoring (categories, products, variants, size
  guide, product image galleries);
- anonymous public storefront reads (categories, products, product detail);
- anonymous cart, checkout-summary, order creation, sandbox payment and
  order-lookup flows;
- tenant-scoped shipping-rate and coupon administration;
- the tenant storefront identity and customer policy pages (one profile per
  tenant, published or draft);
- the permission-gated tenant-operator order reads (list + detail, B042);
- the `Shop.Catalog.Manage`/`Shop.Shipping.Manage`/`Shop.Settings.Manage`/
  `Shop.Orders.View` permission keys, enforced by Shop's own authorization
  code, plus the reserved `Shop.Orders.Manage` key (registered, not yet
  enforced — B043 owns its mutations).

Shop explicitly does **not** own:

- frontend presentation, routing or visual design;
- IAM's accounts, authentication, tenant membership or role storage — Shop
  reads IAM's tables with its own raw SQL (see
  [Section 8](#8-tenantauth-rules)) instead of referencing IAM;
- cloud object storage, video, image-cropping UI or CDN signing for product
  media (see [Section 10](#10-product-media));
- real payment-gateway integration (only an in-app sandbox exists — see
  [Section 13](#13-sandbox-payment));
- customer accounts (every Shop-facing flow outside the authenticated admin
  routes is deliberately anonymous, matching a guest-first storefront).

## 2. Fast facts

| Fact | Value |
| --- | --- |
| Module assembly/path | `src/modules/shop/TenantForge.Modules.Shop/` (`TenantForge.Modules.Shop.csproj`) |
| Public composition seam | `TenantForge.Modules.Shop.ShopModule` — `AddShopModule` (before `Build`), `UseShopModuleAsync` (after `Build`) |
| Database/context | PostgreSQL via `TenantForge.Modules.Shop.Infrastructure.ShopDbContext`, same physical database as IAM but its own `__ShopMigrationsHistory` table |
| Identifier representations | PostgreSQL `bigint` ↔ .NET `Tsid` ↔ HTTP canonical 13-char string — same seam IAM uses, `TenantForge.BuildingBlocks.Identifiers.TsidId` |
| No `.Contract` project | Shop's HTTP request/response records live beside each feature (`features/<area>/<Area>Contracts.cs`), not in a separate project — no second .NET consumer has proved that boundary yet |
| Permission model | `Shop.Catalog.Manage`, `Shop.Shipping.Manage`, `Shop.Settings.Manage`, `Shop.Orders.View` (enforced) + `Shop.Orders.Manage` (reserved for B043) — tenant Owner bypass, or an assigned `TenantRole` carrying the key (IAM-owned role storage, Shop-owned check) — see [Section 8](#8-tenantauth-rules) |
| Test project | `tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj` |
| Local SDK/runtime notes | No Linux `dotnet`; use `dotnet.exe` — see `docs/architecture.md#local-development-environment-wsl--windows-net-sdk` |
| Primary handbook update rule | Every Shop-touching backend task updates this file or states `SHOP.md impact: none — <specific reason>` — see [Section 17](#17-change-impact-checklist) |

## 3. Dependency and composition boundary

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.Modules.Iam.Contract
        -> TenantForge.BuildingBlocks
    -> TenantForge.Modules.Shop
        -> TenantForge.BuildingBlocks
```

Shop never references IAM or `TenantForge.Modules.Iam.Contract`, and IAM
never references Shop — the two business modules share only
`TenantForge.BuildingBlocks`. Shop reads IAM's tenant-membership/role tables
directly with raw SQL (`ShopAuthorization`, see
[Section 8](#8-tenantauth-rules)) because both modules point at the same
physical PostgreSQL database (a named B025 decision), not through a project
reference.

The API host composes Shop through exactly two calls
(`src/api/TenantForge.Api/Program.cs`), in the same
registration-before-`Build` / activation-after-`Build` order IAM uses:

```csharp
builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

builder.Services.AddSingleton<IAggregatedPermissionCatalog>(sp =>
    new AggregatedPermissionCatalog(sp.GetServices<IPermissionCatalogContributor>()));

var app = builder.Build();

await app.UseIamModuleAsync();
await app.UseShopModuleAsync();
```

- **`AddShopModule`** (registration, before `Build`) — adds every
  Shop-owned service to the container (`ShopDbContext`, the sandbox payment
  gateway, `IShopMediaStorage`, `ShopImageValidator`, `TimeProvider.System`,
  `IShopCartExpiryService`, `ShopCartCleanupWorker`,
  `ShopPermissionCatalogContributor`). No I/O, no pass/fail decision.
- **`UseShopModuleAsync`** (activation, after `Build`) — runs once, in this
  deterministic order:
  1. validate the fully assembled configuration (fail closed) —
     `ShopConfig.ValidateConfiguration`;
  2. apply pending migrations (`db.Database.MigrateAsync()`);
  3. map every Shop endpoint (`MapShopModule`).

  There is no seed step (B025 decided against seed catalog data). Every
  mapping call inside `MapShopModule` is fixed order: categories, products,
  product media, storefront catalog, shipping rates, coupons, carts,
  checkout, order creation, payments, order lookup.

`ShopConfig` (`src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs`)
implements `TenantForge.BuildingBlocks.Modules.IModuleConfig`: `SectionName`
(`"Shop"`), `RegisterServices`, `ValidateConfiguration`. Every Shop feature
class is `internal`; the host only calls the two `ShopModule` methods above.

## 4. Source-code map

| Concern | Path | Inspect for |
| --- | --- | --- |
| Shop composition seam | `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs` | The two public calls, activation order, fixed feature-mapping order |
| Shop configuration | `src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs` | `Shop:ShopDb`/`Shop:MediaRoot` keys, DI registrations, fail-closed validation |
| Domain entities | `src/modules/shop/TenantForge.Modules.Shop/domain/` | `ShopCategory`, `ShopProduct`, `ShopProductImage`, `ShopProductVariant`, `ShopSizeGuideColumn/Row/Cell`, `ShopShippingRate`, `ShopCoupon`, `ShopCart`, `ShopCartItem`, `ShopOrder`, `ShopOrderItem`, `ShopPaymentAttempt` and their enums |
| Authorization | `src/modules/shop/TenantForge.Modules.Shop/features/authorization/` | `ShopAuthorization` (membership + permission check via raw SQL against IAM's tables), `ShopPermissionCatalogContributor` (the `shop` catalog group) |
| Category admin | `src/modules/shop/TenantForge.Modules.Shop/features/categories/` | `CategoriesFeature` (create/list/update, the `ValidateParentAsync` eligibility rule and the reparent guard, a `FOR UPDATE` row lock so the guard + save are atomic), `CategoryContracts`, `CategoryVisibility` (the shared "effective public activity" predicate used by every public read) |
| Product admin | `src/modules/shop/TenantForge.Modules.Shop/features/products/` | `ProductsFeature` (combined product + variants + size-guide authoring), `ProductContracts` |
| Product media | `src/modules/shop/TenantForge.Modules.Shop/features/media/` | `ProductMediaFeature`, `ProductMediaContracts`, `IShopMediaStorage`/`LocalShopMediaStorage`, `ShopImageValidator` — see [Section 10](#10-product-media) |
| Public storefront reads | `src/modules/shop/TenantForge.Modules.Shop/features/storefront/` | `StorefrontCatalogFeature`, `StorefrontContracts` — the first anonymous endpoints in TenantForge |
| Shipping-rate admin | `src/modules/shop/TenantForge.Modules.Shop/features/shipping/` | `ShippingRatesFeature`, `ShippingRateContracts` |
| Coupon admin | `src/modules/shop/TenantForge.Modules.Shop/features/coupons/` | `CouponsFeature`, `CouponContracts`, `ShopCouponPolicy` (the single pure owner of every coupon rule) — see [Section 12](#12-coupon-rules) |
| Cart | `src/modules/shop/TenantForge.Modules.Shop/features/carts/` | `CartsFeature`, `CartContracts` — see [Section 11](#11-inventory-reservation) |
| Checkout summary | `src/modules/shop/TenantForge.Modules.Shop/features/checkout/` | `CheckoutFeature`, `CheckoutContracts` (read/compute-only) |
| Order creation | `src/modules/shop/TenantForge.Modules.Shop/features/orders/` | `OrderCreationFeature`, `OrderContracts`, `OrderLookupFeature`, `OrderLookupContracts` |
| Admin order reads | `src/modules/shop/TenantForge.Modules.Shop/features/orders/` (B042) | `AdminOrdersFeature` (`Shop.Orders.View`-gated list + detail, non-leaking 404, 20-attempt cap), `AdminOrderContracts` |
| Sandbox payment | `src/modules/shop/TenantForge.Modules.Shop/features/payments/` | `PaymentsFeature`, `PaymentContracts`, `IShopPaymentGateway`, `SandboxPaymentGateway` — see [Section 13](#13-sandbox-payment) |
| Storefront profile & policies | `src/modules/shop/TenantForge.Modules.Shop/features/profiles/` | `ProfilesFeature`, `ProfileContracts` — see [Section 14](#14-storefront-profile-and-policies) |
| Pagination | `src/modules/shop/TenantForge.Modules.Shop/features/pagination/` | `PaginationSupport`, `PaginationQuery`, `PaginationMetadata` (Shop's own copy — not shared with IAM's) |
| Persistence context/maps | `src/modules/shop/TenantForge.Modules.Shop/infrastructure/` | `ShopDbContext`, `*Map.cs`, `ShopTsidValueConverter` |
| Migrations | `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` | Chronological schema history, see [Section 7](#7-persistence) |
| Integration test fixtures | `tests/integration/TenantForge.Api.IntegrationTests/ApiFactory.cs`, `IamDbFixture.cs` | `WebApplicationFactory` setup, per-suite Postgres fixtures (Shop uses its own isolated collections) |
| Focused test classes | `tests/integration/TenantForge.Api.IntegrationTests/Shop*.cs` | See [Section 15](#15-test-map-and-commands) |
| Persistent HTTP contract reference | `docs/design/shop/http-contracts.md` | The wire-shape source of truth per slice, kept in step with delivered code |
| BuildingBlocks permissions seam | `src/building-blocks/TenantForge.BuildingBlocks/Permissions/` | `IPermissionCatalogContributor`, `PermissionGroup`, `PermissionDescriptor`, `IAggregatedPermissionCatalog` |

## 5. Configuration

All keys are read by `ShopConfig`
(`src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs`).

| Key | Owner | Required? | Failure mode |
| --- | --- | --- | --- |
| `Shop:ShopDb` | Shop | Always required | Startup throws `InvalidOperationException` (fail closed) if blank |
| `Shop:MediaRoot` | Shop | Always required | Startup throws if blank, non-absolute, or the directory cannot be created/written (`LocalShopMediaStorage.ValidateRoot`) |
| `Shop:CartReservationMinutes` | Shop | Required outside Development; Development defaults to `30` when blank | Startup throws unless the integer value is within `5..1440` inclusive |
| `Shop:CartCleanupIntervalSeconds` | Shop | Always required | Startup throws unless the integer value is within `30..3600` inclusive |

`Shop:ShopDb` deliberately points at the same physical database as
`IAM:IamDb` (see [Section 3](#3-dependency-and-composition-boundary)); each
module's migration history is tracked in its own table
(`__ShopMigrationsHistory` vs IAM's default) so the two histories cannot
collide. `Shop:MediaRoot` is an absolute filesystem directory that must be
writable by the running process — see [Section 10](#10-product-media).

## 6. Domain model and invariants

All entities live in `src/modules/shop/TenantForge.Modules.Shop/domain/` and
are `internal` (not reachable outside the module).

| Entity | Identity | Key relationships | Invariants |
| --- | --- | --- | --- |
| `ShopCategory.cs` | `Tsid Id` | optional self-FK `ParentCategoryId` (nullable; `Restrict` — a parent with children cannot be deleted/re-keyed) | `Slug` lower-cased; unique per tenant; `ParentCategoryId` is `null` for a root and otherwise names an active root of the same tenant (max depth root + one child); a category that has children can never itself become a child (feature-enforced `409`); public visibility is "effective activity" — `IsActive` AND, for children, the root's `IsActive` (see [Section 9](#9-endpoint-catalog)) |
| `ShopProduct.cs` | `Tsid Id` | `CategoryId` | `Slug` lower-cased, unique per tenant; `GalleryVersion` (int, starts at `1`) — incremented exactly once per gallery mutation, used as an optimistic-concurrency guard on every upload/reorder/delete; `IsActive` excludes it from public storefront reads |
| `ShopProductImage.cs` | `Tsid Id` | `ProductId` (cascade delete) | Server-generated `StorageKey` (never a client filename); `ContentType` fixed to `"image/webp"`; unique `(ProductId, DisplayOrder)`; non-unique `(TenantId, ProductId)`; no original filename or filesystem path stored — see [Section 10](#10-product-media) |
| `ShopProductVariant.cs` | `Tsid Id` | `ProductId` | `StockQuantity` decremented atomically on cart-add, released on cart removal — see [Section 11](#11-inventory-reservation); `PriceOverride` optional, falls back to the product's `BasePrice` |
| `ShopSizeGuideColumn.cs`/`ShopSizeGuideRow.cs`/`ShopSizeGuideCell.cs` | `Tsid Id` each | → `ShopProduct`/`ShopSizeGuideRow` | Replaced wholesale on every product update (no partial edit) |
| `ShopShippingRate.cs` | `Tsid Id` | none | One rate per `(TenantId, ProvinceName)` |
| `ShopCoupon.cs` | `Tsid Id` | none | `Code`/`NormalizedCode` (upper-cased) unique per tenant; `DiscountType` is `Percentage` or `FixedAmount`; `Deactivate()` is the only state-removal path (no hard delete); B041 adds `MinimumSubtotal` (≥0), `MaximumDiscountAmount` (nullable, caps the discount), `RedemptionLimit` (nullable, `null`=unlimited, else `1..1,000,000`), `RedeemedCount` (≥0, only ever incremented atomically at order creation) and a client-managed `Version` (starts at `0`, bumped on every admin save incl. deactivate) — see [Section 12](#12-coupon-rules) |
| `ShopCart.cs` | `Tsid Id` | optional `CouponId` | Anonymous — ownership is by opaque cart id alone, no account link; `Status` is `Active`/`Converted`/`Expired`, `LastTouchedAtUtc` and `ExpiresAtUtc` define the server-owned reservation lease, and `ClosedAtUtc` is set when the cart is converted or expired |
| `ShopCartItem.cs` | `Tsid Id` | `CartId`, `ProductVariantId` | `UnitPriceSnapshot` frozen at add-time; `Quantity` must stay positive |
| `ShopOrder.cs` | `Tsid Id` | none (snapshots cart data, no live FK back to cart) | `Status` (`PendingPayment`/`Paid`/`Cancelled`/`Fulfilled`); `OrderNumber` and `TrackingCode` are generated, unique, unguessable strings; every money/address field is a point-in-time snapshot; `Version` (int, default `1`) is the optimistic-concurrency token B043's order mutations will bump — B042 only persists and returns it |
| `ShopOrderItem.cs` | `Tsid Id` | `OrderId` | Snapshots product name/variant label/unit price at order-creation time — never a live join back to the catalog |
| `ShopPaymentAttempt.cs` | `Tsid Id` | `OrderId` (no `TenantId` column — filter through `ShopOrder` when tenant-scoping is required) | `Status` (`Initiated`/`Succeeded`/`Failed`); `TryResolve` is idempotent — a second callback for an already-resolved attempt returns `false` and changes nothing |
| `ShopProfile.cs` | `Tsid Id` | `TenantId` (unique — exactly one profile row per tenant, enforced by a unique index, not a relationship) | All text is plain text (never HTML); every field is trimmed on the outside only, internal newlines preserved; `InstagramUrl` (nullable) must be HTTPS on `instagram.com`/a subdomain; `SupportPhone` is a conservative display allowlist (digits, spaces, `+`, `-`, `(`, `)`); `Version` is a client-managed optimistic-concurrency counter (starts at `1`, not a DB rowversion); `IsPublished` gates only the public profile/policy reads, never the catalog — see [Section 14](#14-storefront-profile-and-policies) |

## 7. Persistence

`ShopDbContext`
(`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`)
is the single PostgreSQL-backed context (Npgsql provider, configured in
`ShopConfig.RegisterServices`), sharing the physical database with
`IamDbContext` but tracked under its own `__ShopMigrationsHistory` table.

Migration order (`infrastructure/Migrations/`, chronological):
`InitialShopCatalog` → `AddShopCart` → `AddShopShippingRatesAndCoupons` →
`AddShopOrders` → `AddShopPaymentAttempts` → `AddShopProductMedia` →
`AddShopCategoryHierarchy` → `AddShopProfile` →
`AddShopCartReservationExpiry` → `AddShopCouponRules` →
`AddShopOrderVersion`. `AddShopOrderVersion` adds the single
`shop_orders.version` column (int, not nullable, default `1`) — the
optimistic-concurrency token B043's order mutations will bump; B042 only
persists and returns it. `AddShopCouponRules` adds
five columns to `shop_coupons` — `minimum_subtotal` (numeric, default `0`),
`maximum_discount_amount` (numeric, nullable), `redemption_limit` (int,
nullable), `redeemed_count` (int, default `0`) and `version` (int, default
`0`) — backfilling every existing row to the Spec's defaults (unlimited, zero
redeemed, version zero) so pre-B041 coupons keep working unchanged.
`AddShopCartReservationExpiry` adds
`status`, `last_touched_at_utc`, `expires_at_utc` and nullable
`closed_at_utc` to `shop_carts`, backfills existing carts as `Active` with a
30-minute lease from migration time, and creates
`ix_shop_carts_status_expires_at_utc` for deterministic cleanup batches.
`AddShopProfile` adds the
`shop_profiles` table (one row per tenant: the text/policy fields,
`is_published`, the client-managed `version` integer, and the
`created_at_utc`/`updated_at_utc` timestamps) plus the unique index
`ix_shop_profiles_tenant_id` on `tenant_id` — the constraint that resolves a
concurrent first-create to exactly one winner. `AddShopProductMedia` adds the
`shop_product_images` table (unique `(product_id, display_order)`, unique
`storage_key`, non-unique `(tenant_id, product_id)`) and the
`shop_products.gallery_version` column (default `1`).
`AddShopCategoryHierarchy` adds the nullable `shop_categories
.parent_category_id` column, its self-FK to `shop_categories.id` with
`DeleteBehavior.Restrict`, the supporting index
`IX_shop_categories_parent_category_id`, and the lookup index
`ix_shop_categories_tenant_parent_display_order`
(`(tenant_id, parent_category_id, display_order)`). Existing rows keep
`parent_category_id = NULL` (i.e. they remain roots); no IDs or slugs change.

Every `Tsid`-typed column uses `ShopTsidValueConverter.Shared` with
`.ValueGeneratedNever()` on primary keys — the same converter pattern IAM
uses with its own converter instance. Activation always calls
`db.Database.MigrateAsync()` before mapping endpoints.

No Shop table is read or written directly by code outside this module, and
Shop never opens IAM's `IamDbContext` — cross-module reads go through raw SQL
against IAM's known table/column names (see [Section 8](#8-tenantauth-rules)),
not through IAM's context or entities.

## 8. Tenant/auth rules

**Two route families**:

- `/api/tenants/{tenantId}/shop/...` — authenticated, tenant-scoped admin
  routes (category/product/media/shipping-rate/coupon authoring, and the
  storefront profile/policy read + save).
- `/api/shop/{tenantId}/...` — anonymous, public routes (storefront reads,
  cart, checkout summary, order creation, payment initiate/callback, order
  lookup, and the public storefront profile). These were the first anonymous
  endpoints in TenantForge (B027).

**`ShopAuthorization.AuthorizeTenantAccessAsync`**
(`src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`)
is the shared gate for every authenticated admin route. It has two overloads:

- a 3-argument, membership-only overload (every read-only admin route);
- a 4-argument overload taking a `permissionKey` (every mutating admin
  route).

Both run the same active-account/active-tenant/active-membership raw-SQL
join against `iam_tenant_memberships`/`iam_accounts`/`iam_tenants` (Shop
cannot use IAM's `IamDbContext` or entities — see
[Section 3](#3-dependency-and-composition-boundary)). Every authenticated
admin route chain-ends with `.RequireAuthorization()`, so an **unauthenticated**
caller is answered `401` by the JWT challenge before the handler runs; a
missing/invalid route `tenantId` or no active membership from an
**authenticated** caller is `Results.Forbid()` → `403`. No result leaks
whether the tenant exists.

**Permission check**: when a `permissionKey` is supplied, a tenant `Owner`
membership is granted automatically (bypasses the key check entirely); a
plain `Member` must hold an assigned `TenantRole` whose `PermissionKeys`
array contains that key — resolved with a second raw-SQL query
(`ResolveAssignedShopKeysAsync`) against
`iam_tenant_member_role_assignments`/`iam_tenant_roles`.

**Permission catalog contribution**: `ShopPermissionCatalogContributor`
registers one group (`"shop"`) with five keys — `Shop.Catalog.Manage`,
`Shop.Shipping.Manage`, `Shop.Settings.Manage`, `Shop.Orders.View` and
`Shop.Orders.Manage` — through `IPermissionCatalogContributor`. The host
aggregates every registered contributor's groups into
`IAggregatedPermissionCatalog`, served by IAM's `GET /api/permissions/catalog`
— Shop does not expose its own catalog endpoint.

| Permission key | Gated endpoints |
| --- | --- |
| `Shop.Catalog.Manage` | `POST/PUT /api/tenants/{tenantId}/shop/categories*`, `POST/PUT /api/tenants/{tenantId}/shop/products*`, every product-media mutation (`POST`/`PUT`/`DELETE` under `.../images*`) |
| `Shop.Shipping.Manage` | `POST /api/tenants/{tenantId}/shop/shipping-rates`, `POST`/`PUT /api/tenants/{tenantId}/shop/coupons*`, `PATCH …/coupons/{couponId}/deactivate` |
| `Shop.Settings.Manage` | `PUT /api/tenants/{tenantId}/shop/profile` (the admin profile read is membership-only, like every other read-only admin route) |
| `Shop.Orders.View` | `GET /api/tenants/{tenantId}/shop/orders`, `GET …/orders/{orderId}` — the only Shop admin **reads** gated by a permission key rather than plain membership |
| `Shop.Orders.Manage` | (reserved for B043's order-status mutations — registered in the catalog now, enforced nowhere yet) |

Public/anonymous routes enforce tenant isolation only through the
`{tenantId}` route segment and `IsActive`/status filters on the joined
rows — never a permission check, because there is no authenticated caller
to check. A malformed or cross-tenant id on any Shop route (admin or public)
returns the same non-leaking result (`404` on public byte/detail routes,
`403`/`404` on admin routes) as an unknown one.

## 9. Endpoint catalog

37 routes, one row per literal `Map*` call in
`src/modules/shop/TenantForge.Modules.Shop/features/**`.

| Method & path | Purpose | Auth | Feature file |
| --- | --- | --- | --- |
| `POST /api/tenants/{tenantId}/shop/categories` | Create a category (optional `parentCategoryId`; invalid parent → `400` field error) | `Shop.Catalog.Manage` | `categories/CategoriesFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/categories` | List categories flat (paginated), each row carrying `parentCategoryId` | Membership | `categories/CategoriesFeature.cs` |
| `PUT /api/tenants/{tenantId}/shop/categories/{categoryId}` | Update a category (re-parenting a parent-with-children → `409`) | `Shop.Catalog.Manage` | `categories/CategoriesFeature.cs` |
| `POST /api/tenants/{tenantId}/shop/products` | Create a product with variants + size guide | `Shop.Catalog.Manage` | `products/ProductsFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/products` | List products (paginated) | Membership | `products/ProductsFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/products/{productId}` | Fetch one product | Membership | `products/ProductsFeature.cs` |
| `PUT /api/tenants/{tenantId}/shop/products/{productId}` | Replace a product's variants/size guide wholesale | `Shop.Catalog.Manage` | `products/ProductsFeature.cs` |
| `POST /api/tenants/{tenantId}/shop/products/{productId}/images` | Upload a gallery image | `Shop.Catalog.Manage` | `media/ProductMediaFeature.cs` |
| `PUT /api/tenants/{tenantId}/shop/products/{productId}/images/order` | Reorder a gallery | `Shop.Catalog.Manage` | `media/ProductMediaFeature.cs` |
| `DELETE /api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}` | Delete a gallery image | `Shop.Catalog.Manage` | `media/ProductMediaFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content` | Preview a draft/active image | Membership | `media/ProductMediaFeature.cs` |
| `GET /api/shop/{tenantId}/media/{imageId}` | Serve a published image | Anonymous, active-only | `media/ProductMediaFeature.cs` |
| `GET /api/shop/{tenantId}/categories` | Public category list — **roots only**, each with ordered `children` (empty `[]` for a leaf); a category is public only while it **and** its root are active (effective activity) | Anonymous | `storefront/StorefrontCatalogFeature.cs` |
| `GET /api/shop/{tenantId}/categories/{categorySlug}/products` | Active products in a category (paginated); a **root** slug includes its direct children's products, a **child** slug only its own | Anonymous | `storefront/StorefrontCatalogFeature.cs` |
| `GET /api/shop/{tenantId}/products` | List active products with search/sort/sale-only (paginated); `categorySlug` resolves the same way (root includes direct children) | Anonymous | `storefront/StorefrontCatalogFeature.cs` |
| `GET /api/shop/{tenantId}/products/{productSlug}` | Product detail (variants, size guide, gallery); `404` if the owning category is not effectively active | Anonymous | `storefront/StorefrontCatalogFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/shipping-rates` | List shipping rates | Membership | `shipping/ShippingRatesFeature.cs` |
| `POST /api/tenants/{tenantId}/shop/shipping-rates` | Set a province's shipping rate | `Shop.Shipping.Manage` | `shipping/ShippingRatesFeature.cs` |
| `POST /api/tenants/{tenantId}/shop/coupons` | Create a coupon (carries `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`) | `Shop.Shipping.Manage` | `coupons/CouponsFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/coupons` | List coupons | Membership | `coupons/CouponsFeature.cs` |
| `PUT /api/tenants/{tenantId}/shop/coupons/{couponId}` | Update a coupon (optimistic concurrency via `expectedVersion` → `409 stale_version`; `redemptionLimit` below `redeemedCount` → `400`) | `Shop.Shipping.Manage` | `coupons/CouponsFeature.cs` |
| `PATCH /api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate` | Deactivate a coupon (the only state-removal path — no hard delete) | `Shop.Shipping.Manage` | `coupons/CouponsFeature.cs` |
| `POST /api/shop/{tenantId}/carts` | Create an anonymous cart and return its server-owned `expiresAtUtc` lease | Anonymous | `carts/CartsFeature.cs` |
| `POST /api/shop/{tenantId}/carts/{cartId}/items` | Add/merge an item, reserving live stock and extending the lease; expired carts return `410 shop_cart_expired` | Anonymous | `carts/CartsFeature.cs` |
| `PATCH /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` | Update an item's quantity (re-reserving or releasing stock) and extending the lease; expired carts return `410 shop_cart_expired` | Anonymous | `carts/CartsFeature.cs` |
| `DELETE /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` | Remove an item, releasing stock and extending the lease; expired carts return `410 shop_cart_expired` | Anonymous | `carts/CartsFeature.cs` |
| `GET /api/shop/{tenantId}/carts/{cartId}` | Fetch an active cart with computed subtotal and `expiresAtUtc`; read does not extend the lease; expired carts return `410 shop_cart_expired` | Anonymous | `carts/CartsFeature.cs` |
| `POST /api/shop/{tenantId}/checkout/summary` | Price an active cart against an address + optional coupon; expired carts return `410 shop_cart_expired` | Anonymous | `checkout/CheckoutFeature.cs` |
| `POST /api/shop/{tenantId}/orders` | Create an order from an active validated cart, mark the cart `Converted`, and leave the cart row as history | Anonymous | `orders/OrderCreationFeature.cs` |
| `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate` | Start a sandbox payment attempt | Anonymous | `payments/PaymentsFeature.cs` |
| `POST /api/shop/{tenantId}/orders/{orderId}/payments/callback` | Resolve a sandbox payment attempt | Anonymous | `payments/PaymentsFeature.cs` |
| `POST /api/shop/{tenantId}/orders/lookup` | Guest order lookup by tracking code + phone | Anonymous | `orders/OrderLookupFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | List the tenant's orders (paginated, filtered); always sorted `CreatedAtUtc desc, Id desc`; malformed/invalid filter → `400` | `Shop.Orders.View` | `orders/AdminOrdersFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/orders/{orderId}` | One order's full detail (customer, totals, item snapshots, ≤20 newest payment attempts, `version`); malformed/foreign/missing id → one identical non-leaking `404` | `Shop.Orders.View` | `orders/AdminOrdersFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/profile` | Read the tenant's storefront profile — `200 { profile: null }` before the first save | Membership | `profiles/ProfilesFeature.cs` |
| `PUT /api/tenants/{tenantId}/shop/profile` | Create/update the profile (optimistic concurrency via `expectedVersion`) | `Shop.Settings.Manage` | `profiles/ProfilesFeature.cs` |
| `GET /api/shop/{tenantId}/profile` | Public storefront profile — `404` when missing or unpublished | Anonymous, published-only | `profiles/ProfilesFeature.cs` |

All errors use the repository's RFC 7807 Problem Details shape
(`Results.Problem`/`ValidationProblem`) — no second error format exists in
this module.

## 10. Product media

Introduced by B036. Every gallery mutation (`upload`/`reorder`/`delete`)
requires the caller-supplied `ExpectedGalleryVersion` to match
`ShopProduct.GalleryVersion` exactly, runs inside one database transaction,
and increments `GalleryVersion` exactly once on success — a stale version
returns `409 Conflict`, never a silent overwrite. A gallery is capped at
eight images.

**Validation** (`ShopImageValidator`) decodes the actual bytes — never
trusts the filename extension or the `Content-Type` header. It uses
`SixLabors.ImageSharp` (pinned `3.1.11`, Six Labors Split License — Apache
2.0 terms apply to this project's usage; see the B036 learning note for the
full license reasoning) to:

- accept only JPEG/PNG/WebP, rejecting SVG and any format `Image.DetectFormatAsync`
  cannot recognize (caught as `UnknownImageFormatException`) or cannot decode
  (caught as `ImageFormatException`) — both map to a clean `415`, never an
  unhandled exception;
- reject animated/multi-frame images (`image.Frames.Count != 1`);
- reject input over 5 MiB, wider or taller than 4096px, or over 16 million
  total pixels — the 5 MiB check runs first, streamed, so an oversized upload
  never fully buffers before rejection;
- strip `ExifProfile`/`IccProfile`/`XmpProfile` and re-encode to WebP
  (quality 82) before anything is staged to disk.

**Storage** (`LocalShopMediaStorage`) resolves `Shop:MediaRoot` to an
absolute path at activation (`ValidateRoot`, fail-closed if missing,
relative, or unwritable). Every accepted upload is staged to a `.staging`
subdirectory under a server-generated random `StorageKey` (24 random bytes,
hex-encoded, `.webp` suffix) — the client-submitted filename never reaches
the filesystem. The staged file is committed (moved to its final path) only
after the database transaction that persists the `ShopProductImage` row
succeeds; if the transaction fails, the staged file is deleted in the
handler's `catch` block. A removed image's file is deleted only after the
delete's transaction commits. `LocalShopMediaStorage` never registers
`UseStaticFiles` — every byte is served through the two Shop-owned routes
below.

**Serving two ways**:

- the protected route (`GET .../images/{imageId}/content`) lets a
  membership-only caller preview drafts (inactive products/categories still
  serve); sets `X-Content-Type-Options: nosniff` and `Cache-Control:
  no-store`;
- the public route (`GET /api/shop/{tenantId}/media/{imageId}`) joins
  image → product → category and serves only when all three are the same
  tenant, the product is `IsActive`, and the category is **effectively
  active** (itself active, and — when it is a child — its root active; the
  shared `CategoryVisibility` predicate, see [Section 9](#9-endpoint-catalog));
  sets `X-Content-Type-Options: nosniff` and a one-year immutable
  `Cache-Control`, since a `StorageKey` never changes once assigned.

The upload endpoint binds `IFormFile` through `[FromForm]` (multipart, not
JSON) and calls `.DisableAntiforgery()` — it is already protected by the
route's JWT/permission check, so ASP.NET Core's default anti-CSRF middleware
for form-posting endpoints is deliberately turned off rather than left on or
replaced with cookie-based CSRF middleware.

**Response shape**: `ProductResponse` (admin) and the storefront
summary/detail responses (`StorefrontProductSummaryResponse`,
`StorefrontProductDetailResponse`) all carry an ordered `Images` list
(`ProductImageResponse[]`) — empty for a product that has never used these
endpoints, so every response shape from before B036 still deserializes
unchanged. The first ordered image is the storefront card thumbnail.

**Reorder implementation note**: PostgreSQL checks the unique
`(ProductId, DisplayOrder)` index immediately, not deferred to commit. A
non-compacting reorder (for example swapping two images' positions) is
therefore applied in two `SaveChangesAsync` calls inside the same
transaction — first moving every affected row to a temporary, guaranteed-
unique negative `DisplayOrder` (`ShopProductImage.MoveToTemporarySlot`, no
real image ever has a negative order), then assigning the real 0-based
order — so no intermediate state can collide.

## 11. Inventory reservation

Stock is reserved the moment an item is added to a cart
(`CartsFeature`, B028): a guarded, atomic `ExecuteUpdateAsync` decrements
`ShopProductVariant.StockQuantity` only if enough stock remains, so two
concurrent adds against the last unit resolve to exactly one `200` and one
`409 Insufficient stock` — never negative stock. Removing or reducing an
item releases the corresponding quantity back to the variant.

Order creation (`OrderCreationFeature`) does **not** decrement stock a second
time — it snapshots the already-reserved cart into a `ShopOrder` plus one
`ShopOrderItem` per line inside one transaction, generates `OrderNumber` and
`TrackingCode`, removes the cart items, marks the cart `Converted`, and keeps
the cart row as history. A double order from the same cart resolves to exactly
one persisted order.

Cart reservation expiry (B040) is server-owned. `CreateCartResponse` and
`CartResponse` include `expiresAtUtc`; no request body accepts a client clock or
lease length. Successful add/update/delete mutations extend
`LastTouchedAtUtc`/`ExpiresAtUtc`; a plain GET does not. `IShopCartExpiryService
.EnsureActiveAsync` locks a cart before checkout/order/cart-read work and, when
an active cart is due, atomically marks it `Expired`, restores grouped variant
stock, removes cart item rows and commits. The background
 `ShopCartCleanupWorker` calls `ExpireDueAsync` in deterministic `(status,
 expires_at_utc, id)` batches; converted carts are ignored. Expired carts return
 RFC 7807 `410 Gone` with `type: "shop_cart_expired"` on cart, checkout-summary
 and order-creation routes.

## 12. Coupon rules

Introduced by B041. A coupon can now carry a minimum subtotal, a maximum
discount cap and a total redemption limit, and every rule is centralized in one
pure owner, `ShopCouponPolicy`
(`src/modules/shop/TenantForge.Modules.Shop/features/coupons/ShopCouponPolicy.cs`),
so the two call sites (checkout preview and order consumption) cannot drift.

- **Fields** (`ShopCoupon`): `MinimumSubtotal` (decimal, ≥ 0),
  `MaximumDiscountAmount` (nullable decimal, ≥ 0 when set — caps the computed
  discount), `RedemptionLimit` (nullable int — `null` means unlimited; a set
  value must be `1..1,000,000`), `RedeemedCount` (int, ≥ 0), and `Version`
  (client-managed optimistic-concurrency counter, starts at `0`, bumped on every
  admin save including deactivate). Constraints are enforced in validation code;
  the Shop module uses no database check constraints, so the Spec's "also as a
  database constraint if the module already uses them" clause does not apply.
- **`ShopCouponPolicy.Evaluate(coupon, subtotal, nowUtc)`** is the single rule
  owner. It is **pure**: it reads the already-loaded coupon and returns a
  `CouponEvaluation` (valid flag, computed `DiscountAmount`, and a stable
  `ErrorCode`). It never writes. The `coupon_not_found` case is **not** produced
  by `Evaluate` — the caller looks the coupon up with a tenant-first predicate
  and, when the lookup is null, returns `coupon_not_found` itself, so "does not
  exist" and "belongs to another tenant" are indistinguishable on the wire
  (non-leaking). The other rules are checked in a fixed order, stopping at the
  first failure, returning exactly one of: `coupon_inactive`, `coupon_expired`
  (strictly past `ExpiresAtUtc`), `coupon_minimum_not_met`
  (`subtotal < MinimumSubtotal`), `coupon_limit_reached`
  (`RedemptionLimit` set and `RedeemedCount >= RedemptionLimit`).
- **Discount computation** (only when every gate passes): the raw discount is
  the percentage (`subtotal * DiscountValue / 100`, rounded to 2dp) or the fixed
  amount; it is then clamped down first to `MaximumDiscountAmount` (when set)
  and then never past `subtotal` itself — the applied discount can never exceed
  the goods.
- **Preview vs consume** lives in the caller, not in `Evaluate`:
  - checkout summary (`POST …/checkout/summary`) calls `Evaluate` in preview
    mode and renders the result. It writes nothing and never increments
    `RedeemedCount`.
  - order creation (`POST …/orders`) calls `Evaluate` **again inside its
    existing transaction**, after the cart `FOR UPDATE` lock, on a coupon row
    loaded **tracked and locked `FOR UPDATE`** (lock order cart → coupon,
    consistent, so no deadlock). On a valid result it calls
    `coupon.RecordRedemption()` — exactly one increment, under the lock — and
    creates the order with the computed discount. Two concurrent checkouts for
    the last slot serialize on the row lock: the winner commits, the loser
    re-reads the bumped count, fails `coupon_limit_reached`, rolls back, and its
    cart is left active so it can retry without the coupon.
- **Rejected-coupon response**: on the anonymous checkout/order routes a failed
  evaluation surfaces as a `400 ValidationProblem` with a `couponCode` field
  error carrying the exact stable code in parentheses
  (e.g. `(coupon_limit_reached)`); the first three reasons keep the historical
  "not valid" phrasing so B030/B031 tests stay green.
- **Admin update** (`PUT /api/tenants/{tenantId}/shop/coupons/{couponId}`,
  gated by `Shop.Shipping.Manage`): requires an `ExpectedVersion` matching the
  stored `Version` or it is a `409` RFC 7807 `type=stale_version`. It rejects a
  `redemptionLimit` below the current `RedeemedCount` (a `400` naming the field),
  and the code/discount-type immutability after redemption is structural — the
  request carries neither field, so neither can change. Deactivation
  (`PATCH …/deactivate`) is always allowed and also bumps `Version`.

## 13. Sandbox payment

`IShopPaymentGateway` (`features/payments/IShopPaymentGateway.cs`) is the
only seam a real provider would implement. `SandboxPaymentGateway` is the
one registered implementation: `InitiateAsync` never makes an outbound HTTP
call — it mints a `ShopPaymentAttempt` row and a redirect URL to an in-app
frontend route (the fake bank page). `VerifyCallbackAsync` resolves the
attempt to `Succeeded`/`Failed` and is idempotent — a second callback for an
already-resolved attempt changes nothing and reports failure. There is no
stored card data and no gateway webhook signature scheme beyond what the
sandbox needs to demonstrate the seam is real.

## 14. Storefront profile and policies

Introduced by B039. A tenant publishes its store identity and customer
policy pages — `ShopProfile` (one row per tenant, `shop_profiles`) holds
`name`, `tagline`, `support_phone`, `instagram_url` (nullable), `about_text`
and the four policy texts (`shipping`/`payment`/`return`/`privacy`), plus
`is_published`, a client-managed `version` counter and the
`created_at_utc`/`updated_at_utc` timestamps.

- **Admin read** (`GET /api/tenants/{tenantId}/shop/profile`,
  membership-only) returns `200 { profile: null }` before the first save —
  the empty state is a 200, never a 404.
- **Admin save** (`PUT`, `Shop.Settings.Manage`) is an upsert keyed on the
  server-derived route `tenantId` (never a body field). `expectedVersion:
  null` creates (a new row at `version: 1`); a non-null value must match the
  stored row's `version` exactly or the request is a `409` with RFC 7807
  `type=stale_version`. The row is locked `FOR UPDATE` inside a transaction so
  concurrent updates serialize, and the unique `ix_shop_profiles_tenant_id`
  index is what actually resolves two racing first-creates to one success and
  one `409` (a `DbUpdateException` with PostgreSQL SQLSTATE `23505` is caught,
  the transaction rolled back, and the same `stale_version` problem returned).
- **Public read** (`GET /api/shop/{tenantId}/profile`, anonymous) returns the
  profile only while it exists **and** `is_published`; missing or unpublished
  are the same non-leaking `404`. The public response
  (`PublicShopProfileResponse`) deliberately omits `id`, `tenantId`, `version`
  and `updatedAtUtc` — the internal concurrency counter never leaves the
  module on the public wire.
- **Publication gates only the profile/policy pages**, never the catalog:
  no route reads `is_published` for storefront availability, so existing
  storefront URLs keep working during rollout.

**Validation** (plain text end to end — never rendered as HTML): every field
is trimmed on the outside only, internal newlines preserved; required
`name`(100)/`tagline`(180)/`supportPhone`(30) reject blank; optional long
texts cap at `4000`/`6000`; `supportPhone` allows only digits, spaces, `+`,
`-`, `(`, `)`; a non-null `instagramUrl` must be HTTPS with a host that is
exactly `instagram.com` or a `*.instagram.com` subdomain (`Uri.Host` is
lower-cased, and the suffix check anchors on the dot so
`instagram.com.evil.example` is rejected). Any violation is a `400`
`ValidationProblem` naming the field.

## 15. Test map and commands

| Test class | Protects |
| --- | --- |
| `ShopModuleIntegrationTests.cs` | Composition seam, fail-closed startup, exact table roster, migration/history-table isolation from IAM |
| `ShopCatalogAdminIntegrationTests.cs` | Category/product CRUD, tenant isolation, `Shop.Catalog.Manage` enforcement |
| `ShopCategoryHierarchyIntegrationTests.cs` | B038 one-level category hierarchy: root+child create/update round-trip (persisted `parentCategoryId`), grandchild rejected (`400` `parentCategoryId`), foreign-tenant parent rejected, self-parent rejected, reparenting a parent-with-children rejected (`409`, nothing changes), deactivated root hides its active child (admin list still shows it; public list, by-slug products and all-products all drop it), public list nests roots with ordered `children` (empty `[]` for leaf roots), root slug = root+child products vs child slug = own only (on both product routes), pre-B038 flat rows migrate as roots (`parent_category_id` NULL, id/slug unchanged), and child media bytes 404 once the root is deactivated |
| `ShopProductMediaIntegrationTests.cs` | Upload/reorder/delete round-trip, gallery-version conflicts, eight-image cap, real decode-based validation (fake MIME, SVG, corrupt, oversized, path-traversal filename, EXIF/GPS stripping), tenant/permission denial, protected-vs-public byte routes, staged-file cleanup on a forced DB commit failure, backward-compatible `Images: []` |
| `ShopStorefrontCatalogIntegrationTests.cs` | Anonymous read contract, active-only filtering, malformed-id handling, and storefront discovery (all-products list): tenant isolation, inactive category/product exclusion, case-insensitive trimmed/truncated `q` search, `400` on unknown `sort`, cross-tenant `categorySlug` → `404`, all four sorts with an id tie-break, `saleOnly`, sold-out products ordered last with a `basePrice` fallback, thumbnail = first ordered image, and pagination totals reflecting the filtered set |
| `ShopCartIntegrationTests.cs` | Cart create/add/remove/fetch, atomic stock reservation, concurrent-add race safety, lease extension/read non-extension, expiry stock restoration/idempotency/batch behavior, expired-cart `410 shop_cart_expired`, and tenant-isolated cleanup |
| `ShopShippingCouponAdminIntegrationTests.cs` | Shipping-rate/coupon admin CRUD, `Shop.Shipping.Manage` enforcement |
| `ShopCheckoutIntegrationTests.cs` | Checkout-summary pricing, unshippable-province handling, coupon application |
| `ShopOrderIntegrationTests.cs` | Order creation, no double-decrement, concurrent-order race safety |
| `ShopPaymentIntegrationTests.cs` | Sandbox initiate/callback, idempotent resolution |
| `ShopOrderLookupIntegrationTests.cs` | Guest lookup contract, non-leaking generic not-found |
| `ShopProfileIntegrationTests.cs` | B039 storefront profile: admin GET null-empty-state, create→update→GET round-trip with version bump, stale-version `409 stale_version`, concurrent first-create race (one `200`, one `409`, one row), tenant isolation (A cannot read/write B), `Shop.Settings.Manage` denial + role grant + Owner bypass, outside-trim / preserved-newlines / over-length validation, phone allowlist, Instagram URL rules (non-HTTPS / wrong host / look-alike domain), public `404` for missing/unpublished/malformed-id, public `200` shape with no `version`/`id`/`tenantId`/`updatedAtUtc`, and publication NOT gating the storefront catalog |
| `ShopCouponRulesIntegrationTests.cs` | B041 coupon rules: `ShopCouponPolicy` ordered reason codes (`coupon_inactive`/`_expired`/`_minimum_not_met`/`_limit_reached` + the caller's `coupon_not_found`) with the exact stable code surfaced in the `couponCode` field error, percentage discount capped at `maximumDiscountAmount` (and never past the subtotal), checkout summary never incrementing `RedeemedCount`, order creation incrementing by exactly one and storing the evaluated discount, a forced downstream DB failure rolling the redemption back, the concurrent last-redemption race (one 201 with the discount, one 400 `coupon_limit_reached`, total +1, loser's cart left active), stale `expectedVersion` → `409 stale_version`, `redemptionLimit` below `redeemedCount` → `400` with no persisted change, and tenant B unable to read/update/redeem tenant A's coupon with a byte-identical `coupon_not_found` |
| `ShopCouponRulesMigrationTests.cs` | B041 migration: a pre-`AddShopCouponRules` `shop_coupons` row backfills to `redemption_limit` NULL (unlimited), `redeemed_count` 0 and `version` 0, keeping its original fields so it stays usable |
| `ShopAdminOrdersIntegrationTests.cs` | B042 admin order reads: `Shop.Orders.View` matrix (Owner bypass 200, granted member 200, no-key member 403, `Shop.Orders.Manage`-only member still 403, anonymous 401), tenant isolation (foreign orders never listed, cross-tenant id → the byte-identical generic 404), list filters `q`/`status`/`fromUtc`/`toUtc` valid + invalid (400 naming the field), stable two-page pagination, item-snapshot fidelity after the product is renamed/repriced, the 20-newest payment-attempt cap (21 real attempts → 20 returned, newest first), and the detail shape (customer, totals, items, `version: 1`) |

Commands:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
dotnet.exe test TenantForge.sln --nologo --filter "FullyQualifiedName~ShopProductMediaIntegrationTests"
```

See `docs/architecture.md#local-development-environment-wsl--windows-net-sdk`
for the WSL/Windows host-binding notes instead of repeating them here. The
integration tests use Testcontainers; Docker must be running before any test
command.

## 16. Current limitations

Verified against current code (not aspirational):

- **No `.Contract` project** — Shop's HTTP shapes live beside each feature.
  A second real .NET consumer of a Shop shape would trigger the same
  `module-contract-project` admission decision IAM's Contract project made.
- **Product media is local-disk only** — no cloud object storage, no CDN
  signing, no video, no image-cropping UI (B036's explicit non-goals).
- **No customer accounts** — every public/anonymous route is by design;
  there is no login, no saved address book, no order history beyond the
  guest tracking-code lookup.
- **Sandbox payment only** — no real gateway integration exists yet; see
  [Section 13](#13-sandbox-payment).
- **No full-text search engine, popularity/rating sort, recommendations,
  tags or faceted color/size filters** — B037's storefront discovery is
  name search + the four `newest`/`price-asc`/`price-desc`/`name` sorts only
  (see [Section 9](#9-endpoint-catalog) and `docs/design/shop/http-contracts.md`
  S33).
- **Category hierarchy is exactly two levels deep** — a root plus one direct
  child; grandchild creation is rejected (B038). There is no category
  deletion, no breadcrumbs deeper than two levels and no bulk reordering.
- **No per-customer or product-specific coupons, no stacking, campaigns or
  automatic promotions** — B041 delivers the tenant-wide minimum subtotal,
  maximum-discount cap and total redemption limit only (see
  [Section 12](#12-coupon-rules)); customer-scoped or item-scoped coupons remain
  out of scope until their own slice.
- **No admin order mutations or rate limiting** — B042 delivers read-only
  order list/detail for operators; fulfil/cancel (and `Shop.Orders.Manage`
  enforcement) arrive in B043, and rate limiting in B046 (see
  `tasks/TASKS.md`'s Backend queue for current status; do not treat a
  `planned` row as already-delivered behavior).

## 17. Change-impact checklist

| Change | Mandatory SHOP.md sections |
| --- | --- |
| route/request/response/status | Endpoint catalog |
| config/secret/startup | Configuration; composition/startup |
| entity/invariant | Domain; persistence when mapped |
| table/map/migration | Persistence |
| permission/membership/tenant rule | Tenant/auth rules; affected endpoint rows |
| product media behavior | Product media |
| inventory/stock behavior | Inventory reservation |
| coupon rule/limit/redemption behavior | Coupon rules |
| payment gateway behavior | Sandbox payment |
| storefront profile/policy behavior | Storefront profile and policies |
| test/verification path | Test map |
| implemented limitation | Current limitations |

Self-review declaration formats (use exactly one, in self-review and the PR
body, for every backend task that touches `src/modules/shop/**`):

```text
SHOP.md impact: updated — <sections>
SHOP.md impact: none — <specific reason>
```

B036/S32 declaration: `SHOP.md impact: updated — created this handbook
(Sections 1–15) from current code, covering the composition seam, config
keys, domain model, persistence, tenant/auth rules, the full 28-route
endpoint catalog, the new product-media feature, inventory reservation,
sandbox payment, the test map and current limitations.`
