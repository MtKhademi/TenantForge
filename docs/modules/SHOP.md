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
13. [Payment lifecycle (gateway-neutral)](#13-payment-lifecycle-gateway-neutral)
14. [Storefront profile and policies](#14-storefront-profile-and-policies)
15. [Public abuse controls (rate limiting and request-size bounds)](#15-public-abuse-controls-rate-limiting-and-request-size-bounds)
16. [Test map and commands](#16-test-map-and-commands)
17. [Current limitations](#17-current-limitations)
18. [Change-impact checklist](#18-change-impact-checklist)

## 1. Purpose and non-goals

Shop owns:

- tenant-scoped catalog authoring (categories, products, variants, size
  guide, product image galleries);
- anonymous public storefront reads (categories, products, product detail);
- anonymous cart, checkout-summary, order creation, the gateway-neutral
  payment lifecycle (initiate / token-protected status / Development-only
  sandbox resolve) and order-lookup flows;
- tenant-scoped shipping-rate and coupon administration;
- the tenant storefront identity and customer policy pages (one profile per
  tenant, published or draft);
- the permission-gated tenant-operator order reads (list + detail, B042) and
  the order-status mutations (fulfil/cancel, B043);
- the public-abuse controls on the sensitive anonymous flows — per-policy
  per-minute rate limiting (per tenant + effective IP) on order lookup, cart
  mutation, checkout/order creation and payment initiation, plus the
  request-body / callback-query size bounds (B046);
- the `Shop.Catalog.Manage`/`Shop.Shipping.Manage`/`Shop.Settings.Manage`/
  `Shop.Orders.View`/`Shop.Orders.Manage` permission keys, all enforced by
  Shop's own authorization code (`Shop.Orders.Manage` gates the B043
  order-status route).

Shop explicitly does **not** own:

- frontend presentation, routing or visual design;
- IAM's accounts, authentication, tenant membership or role storage — Shop
  reads IAM's tables with its own raw SQL (see
  [Section 8](#8-tenantauth-rules)) instead of referencing IAM;
- cloud object storage, video, image-cropping UI or CDN signing for product
  media (see [Section 10](#10-product-media));
- payment capabilities beyond the delivered gateways: refunds, inquiry
  schedulers, webhooks, split payments, fee calculation and multiple merchant
  accounts remain outside the module today — see
  [Section 13](#13-payment-lifecycle-gateway-neutral);
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
| Permission model | `Shop.Catalog.Manage`, `Shop.Shipping.Manage`, `Shop.Settings.Manage`, `Shop.Orders.View` and `Shop.Orders.Manage` (all enforced) — tenant Owner bypass, or an assigned `TenantRole` carrying the key (IAM-owned role storage, Shop-owned check) — see [Section 8](#8-tenantauth-rules) |
| Test project | `tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj` |
| Local SDK/runtime notes | No Linux `dotnet`; use `dotnet.exe` — see `docs/architecture.md#local-development-environment-wsl--windows-net-sdk` |
| Primary handbook update rule | Every Shop-touching backend task updates this file or states `SHOP.md impact: none — <specific reason>` — see [Section 18](#18-change-impact-checklist) |

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

The API host composes Shop through its two module calls plus the B046
rate-limiting wiring, all in `src/api/TenantForge.Api/Program.cs`, in the
registration-before-`Build` / activation-after-`Build` order IAM uses:

```csharp
builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

builder.Services.AddSingleton<IAggregatedPermissionCatalog>(sp =>
    new AggregatedPermissionCatalog(sp.GetServices<IPermissionCatalogContributor>()));

// B046: the host's one AddRateLimiter call, contributed by Shop; plus the
// ForwardedHeaders trusted-proxy options (empty = no proxy trusted).
builder.Services.AddShopRateLimiter();
builder.Services.Configure<ForwardedHeadersOptions>(
    builder.Configuration.GetSection("ForwardedHeaders"));

var app = builder.Build();

// B046: the first middleware, then the limiter + body-size guard, all before
// the module activation below maps the endpoints.
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseShopRequestBodySizeLimit();

await app.UseIamModuleAsync();
await app.UseShopModuleAsync();
```

- **`AddShopModule`** (registration, before `Build`) — adds every
  Shop-owned service to the container (`ShopDbContext`, both
  `IShopPaymentGateway` implementations (Sandbox today; ZarinPal in B045) plus
  `IShopPaymentGatewayResolver`, `ShopPaymentCompletionService`,
  `IShopMediaStorage`, `ShopImageValidator`, `TimeProvider.System`,
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
 | Domain entities | `src/modules/shop/TenantForge.Modules.Shop/domain/` | `ShopCategory`, `ShopProduct`, `ShopProductImage`, `ShopProductVariant`, `ShopSizeGuideColumn/Row/Cell`, `ShopShippingRate`, `ShopCoupon`, `ShopCart`, `ShopCartItem`, `ShopOrder`, `ShopOrderItem`, `ShopPaymentAttempt`, `ShopPaymentInitiation`, `ShopOrderOperation` and their enums |
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
| Admin order operations | `src/modules/shop/TenantForge.Modules.Shop/features/orders/` (B043) | `AdminOrdersFeature`'s `PATCH …/orders/{orderId}/status` (`Shop.Orders.Manage`-gated fulfil/cancel, optimistic `expectedVersion`, idempotent via the `shop_order_operations` unique key, exactly-once inventory release on cancel) — see [Section 11](#11-inventory-reservation) |
| Payment lifecycle | `src/modules/shop/TenantForge.Modules.Shop/features/payments/` | `PaymentsFeature`, `PaymentContracts`, `IShopPaymentGateway`, `SandboxPaymentGateway`, `ShopPaymentGatewayResolver`, `ShopPaymentCompletionService`, plus `ZarinPal/` (`ZarinPalOptions`, `ZarinPalPaymentGateway`, `ZarinPalCallbackFeature`, signed callback-state protector and amount converter) — see [Section 13](#13-payment-lifecycle-gateway-neutral) |
| Storefront profile & policies | `src/modules/shop/TenantForge.Modules.Shop/features/profiles/` | `ProfilesFeature`, `ProfileContracts` — see [Section 14](#14-storefront-profile-and-policies) |
| Public abuse controls | `src/modules/shop/TenantForge.Modules.Shop/features/rateLimiting/` | `ShopRateLimitOptions` (public, `Shop:RateLimiting` bind + Production fail-closed), `ShopRateLimiterHostExtensions` (public `AddShopRateLimiter`/`UseShopRequestBodySizeLimit`, the internal `ShopRateLimitPolicies`, the generic `429` handler and the `413` guard) — see [Section 15](#15-public-abuse-controls-rate-limiting-and-request-size-bounds) |
| Pagination | `src/modules/shop/TenantForge.Modules.Shop/features/pagination/` | `PaginationSupport`, `PaginationQuery`, `PaginationMetadata` (Shop's own copy — not shared with IAM's) |
| Persistence context/maps | `src/modules/shop/TenantForge.Modules.Shop/infrastructure/` | `ShopDbContext`, `*Map.cs`, `ShopTsidValueConverter` |
| Migrations | `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` | Chronological schema history, see [Section 7](#7-persistence) |
| Integration test fixtures | `tests/integration/TenantForge.Api.IntegrationTests/ApiFactory.cs`, `IamDbFixture.cs` | `WebApplicationFactory` setup, per-suite Postgres fixtures (Shop uses its own isolated collections) |
| Focused test classes | `tests/integration/TenantForge.Api.IntegrationTests/Shop*.cs` | See [Section 16](#16-test-map-and-commands) |
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
| `Shop:Payments:Provider` | Shop | Required outside Development; Development defaults to `Sandbox` when blank | Startup throws unless the value is exactly `Sandbox` or `ZarinPal` (case-sensitive); outside Development the value is additionally required and `Sandbox` is refused — the browser-driven payment simulation fails closed in Production |
| `Shop:Payments:ZarinPal:MerchantId` | Shop | Required when `Provider=ZarinPal` | Startup throws if blank; the value is a secret and is never logged, returned or persisted per order |
| `Shop:Payments:ZarinPal:Currency` | Shop | Required when `Provider=ZarinPal`; defaults to `IRT` in the options type | Startup throws unless exactly `IRT` or `IRR`; stored Shop totals are Tomans, so `IRT` sends the total as-is and `IRR` sends a checked ×10 conversion |
| `Shop:Payments:ZarinPal:RequestEndpoint` / `VerifyEndpoint` / `GatewayBaseUrl` | Shop | Required when `Provider=ZarinPal` | Startup throws if missing; outside Development each must be HTTPS and its host must be exactly one of `api.zarinpal.com`, `checkout.zarinpal.com`, `dev.zarinpal.com` |
| `Shop:Payments:ZarinPal:PublicApiBaseUrl` / `FrontendResultBaseUrl` | Shop | Required when `Provider=ZarinPal` | Startup throws if missing; outside Development each must be HTTPS. `PublicApiBaseUrl` builds the backend callback URL sent to ZarinPal, and `FrontendResultBaseUrl` is the browser 302 target after verification |
| `Shop:Payments:ZarinPal:TimeoutSeconds` | Shop | Optional when `Provider=ZarinPal`; default `10` | Startup throws unless the integer is `1..300`; the gateway applies it per provider call through cancellation tokens |
| `Shop:RateLimiting:OrderLookupPerMinute` | Shop (B046) | Required outside Development; Development defaults to `30` when blank | Startup throws unless the integer is within `1..10000` |
| `Shop:RateLimiting:CartMutationPerMinute` | Shop (B046) | Required outside Development; Development defaults to `120` when blank | Startup throws unless the integer is within `1..10000` |
| `Shop:RateLimiting:CheckoutOrderPerMinute` | Shop (B046) | Required outside Development; Development defaults to `30` when blank | Startup throws unless the integer is within `1..10000` |
| `Shop:RateLimiting:PaymentInitiationPerMinute` | Shop (B046) | Required outside Development; Development defaults to `20` when blank | Startup throws unless the integer is within `1..10000` |
| `Shop:RateLimiting:MaxRequestBodyBytes` | Shop (B046) | Required outside Development; Development defaults to `524288` (512 KiB) when blank | Startup throws unless the integer is within `1..5242880` (5 MiB) |
| `Shop:RateLimiting:QueueLength` | Shop (B046) | Optional; defaults to `0` | Startup throws unless it is exactly `0` (a non-zero value is refused — the limiter never queues) |
| `ForwardedHeaders:*` | host (B046) | Optional | The trusted-proxy allowlist `UseForwardedHeaders` uses; empty means no proxy is trusted and the direct remote IP is always used |

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
| `ShopOrder.cs` | `Tsid Id` | none (snapshots cart data, no live FK back to cart) | `Status` (`PendingPayment`/`Paid`/`Cancelled`/`Fulfilled`); `OrderNumber` and `TrackingCode` are generated, unique, unguessable strings; every money/address field is a point-in-time snapshot; `Version` (int, default `1`) is the optimistic-concurrency token the status actions bump; `FulfilledAtUtc`/`CancelledAtUtc`/`InventoryReleasedAtUtc` are nullable and each set exactly once (B043); `TryFulfill` (only from `Paid`) and `TryCancel` (only from `PendingPayment`) each check `expectedVersion == Version`, move the status, stamp the matching timestamp and bump `Version` — `MarkInventoryReleased` stamps `InventoryReleasedAtUtc` only when still null (the exactly-once guard); `MarkPaid` (B044, called only by the payment completion service) moves `PendingPayment → Paid` and deliberately does **not** bump `Version` — only operator mutations are `expectedVersion`-gated, so a payment never invalidates an operator's in-flight version |
| `ShopOrderOperation.cs` | `Tsid Id` | `OrderId` (cascade) | B043's idempotency record — one row per order-status action: the client `Key` (a UUID), the canonical `OrderStatusAction` (`Fulfill`/`Cancel`), a JSON `ResponseSnapshot`, the acting `ActorId` and `CreatedAtUtc`; the unique `(TenantId, Key)` constraint makes a key single-use per tenant |
| `ShopOrderItem.cs` | `Tsid Id` | `OrderId` | Snapshots product name/variant label/unit price at order-creation time — never a live join back to the catalog |
| `ShopPaymentAttempt.cs` | `Tsid Id` | `OrderId` (no `TenantId` column — filter through `ShopOrder` when tenant-scoping is required) | `Status` (`Initiated`/`Succeeded`/`Failed`/`Invalidated`); `AmountSnapshot` freezes the order total at initiation; `CallbackTokenHash` stores only the SHA-256 of the 32-byte raw callback token (the raw token is never persisted); `FailureCode` (stable code on `Failed`), `ProviderReference` (the provider's verification-time reference, null until a success — never the authority, which stays in `GatewayReference`), `VerifiedAtUtc` (set exactly once on resolution/invalidation) and `Version` (bumped exactly by the resolving write); `TryResolve` (→`Succeeded`/`Failed`) and `TryInvalidate` (→`Invalidated`, called by a cancel) are both called exclusively by `ShopPaymentCompletionService` and both idempotent — a second call for an already-resolved attempt returns `false` and changes nothing |
| `ShopPaymentInitiation.cs` | `Tsid Id` | `TenantId`, `OrderId` (cascade), `AttemptId` | B044's idempotency record — one row per client `IdempotencyKey` (a UUID) per initiation: the `RequestFingerprint` (SHA-256 of the canonical `tenantId|orderId`), the stored `RedirectUrl`, and the 32-byte `TokenSeed` from which the raw token is re-derived on a same-key replay (the seed is not the token and never authenticates anything); the **unique** `(TenantId, IdempotencyKey)` index makes a key single-use per tenant and is what resolves a racing first-call to exactly one winner |
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
`AddShopOrderVersion` → `AddShopOrderOperations` → `AddShopPaymentLifecycle`.
`AddShopPaymentLifecycle` (B044) adds six columns to
`shop_payment_attempts` — `amount_snapshot` (numeric(12,2), not nullable),
`callback_token_hash` (varchar(64), not nullable), `failure_code`
(varchar(40), nullable), `provider_reference` (varchar(60), nullable),
`verified_at_utc` (nullable) and `version` (int, not nullable, default `0`) —
the three new not-nullable columns take flat defaults (`amount_snapshot` `0`,
`callback_token_hash` empty string, `version` `0`) rather than a data backfill:
pre-B044 attempt rows are historical (already resolved, or — if still
`Initiated` — the completion service's amount/provider re-check rejects them
safely), so no cross-table data migration is needed — and creates the
`shop_payment_initiations` table (`id`, `tenant_id`, `order_id` with a cascade
FK to `shop_orders`, `attempt_id`, `idempotency_key` varchar(36),
`request_fingerprint` varchar(64), `redirect_url` varchar(2048), `token_seed`
(bytea, 32 bytes), `created_at_utc`) with the **unique** index
`ix_shop_payment_initiations_tenant_idempotency_key` on
`(tenant_id, idempotency_key)` and a non-unique
`ix_shop_payment_initiations_order_id`.
`AddShopOrderOperations` adds the three nullable timestamps to `shop_orders`
(`fulfilled_at_utc`, `cancelled_at_utc`, `inventory_released_at_utc`) and the
`shop_order_operations` table (B043's idempotency record: `id`, `tenant_id`,
`order_id` with a cascade FK to `shop_orders`, `idempotency_key`
(varchar(36)), `action` (varchar(20), the string-converted
`OrderStatusAction`), `response_snapshot` (text), `actor_id`,
`created_at_utc`) with the **unique** index
`ix_shop_order_operations_tenant_idempotency_key` on
`(tenant_id, idempotency_key)` — the constraint that makes an idempotency key
single-use per tenant — plus a non-unique `ix_shop_order_operations_order_id`.
`AddShopOrderVersion` adds the single
`shop_orders.version` column (int, not nullable, default `1`) — the
optimistic-concurrency token the status actions bump (B043); B042 only
persisted and returned it. `AddShopCouponRules` adds
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
  cart, checkout summary, order creation, payment initiate/status/sandbox
  resolve, order lookup, and the public storefront profile). These were the
  first anonymous endpoints in TenantForge (B027).

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
| `Shop.Orders.Manage` | `PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status` — B043's order-status mutation (the key B042 registered but left reserved; this route is the one that enforces it) |

Public/anonymous routes enforce tenant isolation only through the
`{tenantId}` route segment and `IsActive`/status filters on the joined
rows — never a permission check, because there is no authenticated caller
to check. A malformed or cross-tenant id on any Shop route (admin or public)
returns the same non-leaking result (`404` on public byte/detail routes,
`403`/`404` on admin routes) as an unknown one.

## 9. Endpoint catalog

39 routes, one row per literal `Map*` call in
`src/modules/shop/TenantForge.Modules.Shop/features/**` (the Development-only
sandbox-resolve route is mapped conditionally but is still a mapped route).

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
| `POST /api/shop/{tenantId}/carts` | Create an anonymous cart and return its server-owned `expiresAtUtc` lease; over the per-minute limit → generic `429` `shop_rate_limit` (`shop-cart-mutation`) | Anonymous, rate-limited | `carts/CartsFeature.cs` |
| `POST /api/shop/{tenantId}/carts/{cartId}/items` | Add/merge an item, reserving live stock and extending the lease; expired carts return `410 shop_cart_expired`; rate-limited (`shop-cart-mutation`) | Anonymous, rate-limited | `carts/CartsFeature.cs` |
| `PATCH /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` | Update an item's quantity (re-reserving or releasing stock) and extending the lease; expired carts return `410 shop_cart_expired`; rate-limited (`shop-cart-mutation`) | Anonymous, rate-limited | `carts/CartsFeature.cs` |
| `DELETE /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` | Remove an item, releasing stock and extending the lease; expired carts return `410 shop_cart_expired`; rate-limited (`shop-cart-mutation`) | Anonymous, rate-limited | `carts/CartsFeature.cs` |
| `GET /api/shop/{tenantId}/carts/{cartId}` | Fetch an active cart with computed subtotal and `expiresAtUtc`; read does not extend the lease; expired carts return `410 shop_cart_expired` | Anonymous | `carts/CartsFeature.cs` |
| `POST /api/shop/{tenantId}/checkout/summary` | Price an active cart against an address + optional coupon; expired carts return `410 shop_cart_expired`; rate-limited (`shop-checkout-order`) | Anonymous, rate-limited | `checkout/CheckoutFeature.cs` |
| `POST /api/shop/{tenantId}/orders` | Create an order from an active validated cart, mark the cart `Converted`, and leave the cart row as history; rate-limited (`shop-checkout-order`) | Anonymous, rate-limited | `orders/OrderCreationFeature.cs` |
| `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate` | Start a payment attempt — `Idempotency-Key` UUID header, no body; `400` naming the header when missing/non-UUID; `409` when the order is not `PendingPayment`; `409 idempotency_key_conflict` on a same-key/different-request reuse; `409 too_many_payment_attempts` on the 11th attempt; a same-key replay returns the stored response byte-identically; a fresh key for an order with a live attempt returns that attempt's response; rate-limited (`shop-payment`) | Anonymous, rate-limited | `payments/PaymentsFeature.cs` |
| `GET /api/shop/{tenantId}/orders/{orderId}/payments/status?token={resultToken}` | Token-protected order status — fixed-time hash comparison; only `orderNumber`/`status`/`providerReference`; every miss (missing/blank/wrong token, wrong order, wrong tenant, no attempts) is the one identical generic `404` | Anonymous | `payments/PaymentsFeature.cs` |
| `POST /api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve` | Development-only sandbox simulation — `{ authority, approved }`; blank `authority` → `400` naming `authority`; a success for an unpayable order → `409`; a success for an already-resolved attempt returns the stored outcome without re-applying | Anonymous, Development-only | `payments/PaymentsFeature.cs` |
| `POST /api/shop/{tenantId}/orders/lookup` | Guest order lookup by tracking code + phone; over the per-minute limit → generic `429` `shop_rate_limit` (`shop-order-lookup`) | Anonymous, rate-limited | `orders/OrderLookupFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | List the tenant's orders (paginated, filtered); always sorted `CreatedAtUtc desc, Id desc`; malformed/invalid filter → `400` | `Shop.Orders.View` | `orders/AdminOrdersFeature.cs` |
| `GET /api/tenants/{tenantId}/shop/orders/{orderId}` | One order's full detail (customer, totals, item snapshots, ≤20 newest payment attempts, `version`); malformed/foreign/missing id → one identical non-leaking `404` | `Shop.Orders.View` | `orders/AdminOrdersFeature.cs` |
| `PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status` | Fulfil (`Paid→Fulfilled`) or cancel (`PendingPayment→Cancelled`); `ChangeOrderStatusRequest { action, expectedVersion }` + `Idempotency-Key` UUID header; `400` for a bad action/key, `409 invalid_order_transition`, `409 stale_version`, `409 idempotency_key_conflict`; cancel restores stock exactly once and invalidates `Initiated` attempts; malformed/foreign/missing id → one identical non-leaking `404` | `Shop.Orders.Manage` | `orders/AdminOrdersFeature.cs` |
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

**Cancel inventory restore (B043)**: the order-status PATCH, when cancelling a
`PendingPayment` order, runs inside one database transaction with the order
row locked `FOR UPDATE` and the referenced variant rows locked `FOR UPDATE`
before the restore — two concurrent cancels of the same order serialize so
stock is restored exactly once. The restore is further guarded by the order's
`InventoryReleasedAtUtc`: it is set in the same transaction only when still
null, so even if cancel were somehow triggered twice, the stock delta is applied
at most once. Fulfil (`Paid→Fulfilled`) never touches stock.

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

## 13. Payment lifecycle (gateway-neutral)

`IShopPaymentGateway` (`features/payments/IShopPaymentGateway.cs`) is the
seam a payment provider implements. Two implementations are registered:
`SandboxPaymentGateway` and `ZarinPalPaymentGateway`. Exactly one is chosen per
request by `ShopPaymentGatewayResolver` from the `Shop:Payments:Provider` value
— never by registration order. A gateway is a pure decision function: it never
sees the order aggregate, never touches the database, and never mutates an
attempt or order.

**Trust boundary**: the browser never declares success. Initiation carries no
body at all — the `Idempotency-Key` header is the only input and every value
(the amount, the provider, the callback URL) is server-derived. The browser
only carries the `authority` it was redirected with; verification goes
`gateway.VerifyAsync` → `ShopPaymentCompletionService`, which re-checks
tenant, order, amount and provider under row locks before anything moves.

**The completion service is the single transition owner.**
`ShopPaymentCompletionService` is the ONLY place an attempt leaves
`Initiated` (to `Succeeded`/`Failed`/`Invalidated`) and the ONLY place an order
moves `PendingPayment → Paid`. It locks the order row (`FOR UPDATE`,
tenant-first — the attempt has no tenant column of its own) then the attempt
row (its unique `gateway_reference`), validates the attempt belongs to that
order, the providers match, the frozen `AmountSnapshot` matches the order's
total, and — for a success — the order is still `PendingPayment`; then it
resolves the attempt exactly once (a racing duplicate blocks on the lock and
returns the already-computed outcome instead of re-applying it). A success
calls `ShopOrder.MarkPaid()` — which deliberately does not bump the order's
`Version`, since only operator mutations are `expectedVersion`-gated.

**Callback token**: a 32-byte random seed is stored on the initiation row; the
raw token is its SHA-256 (returned to the client exactly once, and re-derived
byte-identically on a same-key replay); only the token's SHA-256 is stored, on
the attempt. `GET …/payments/status` is the only route that accepts it, and
compares the presented token's hash with a **fixed-time** comparison (a
normal `==`/`string.Equals` is never used for it).

**Idempotent initiation**: `POST …/payments/initiate` requires the
`Idempotency-Key` header (a UUID, or `400` naming the header). Under the
order-row lock it: rejects a non-`PendingPayment` order (`409`); replays a
same-key/same-request initiation byte-identically (a same-key/different-request
reuse is `409 idempotency_key_conflict`); reuses the order's single live
`Initiated` attempt for any fresh key (no orphan rows); and enforces the 10-attempt
cap (the 11th → `409 too_many_payment_attempts`). The unique
`(tenant_id, idempotency_key)` index on `shop_payment_initiations` is the
constraint that resolves a racing same-key first-call to exactly one winner
(a `23505` is caught and answered as a replay, never a `500`).

**Sandbox gateway**: `InitiateAsync` makes no outbound HTTP call — it mints an
authority (16 random bytes → 32 lowercase hex chars) and the relative,
same-origin redirect `/shop/{tenantId}/bank?authority={authority}` (the real
in-app fake-bank route). `VerifyAsync` maps that page's single `approved`
value to a `GatewayVerification` (success, or `Failed` with a stable code —
`payment_declined` / `verification_failed`). The Development-only
`POST …/payments/sandbox/resolve` route is the only browser-driven payment
simulation: it is mapped only when the environment is Development, and
`Shop:Payments:Provider=Sandbox` is refused at startup outside Development, so
the simulation cannot be enabled silently in Production. There is no stored
card data and no webhook signature scheme — B045's real provider verifies
server-to-server through the same seam.

**ZarinPal gateway and callback**: `ZarinPalPaymentGateway` calls the configured
v4 request endpoint with the merchant id, checked integer amount, currency,
description and a backend callback URL. Request code `100` plus a non-empty
authority is the only initiation success; the authority is stored in the
existing `GatewayReference` column and the browser redirect is
`GatewayBaseUrl + authority`. The real callback route is
`GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`.
The browser does not call verify and never supplies amount/order success:
`state` is an ASP.NET Core Data Protection token (purpose
`TenantForge.Shop.ZarinPal.Callback.v1`, 30-minute lifetime) binding tenant id,
order id, attempt id and the raw callback token. `Status != OK` resolves the
attempt as `Failed` with `payment_declined` and does **not** call ZarinPal
verify. `Status == OK` calls verify server-to-server using the stored authority
and the attempt's frozen `AmountSnapshot`; code `100` succeeds, code `101`
("already verified") is accepted only when it matches a success reference already
stored for that same attempt, and every other code fails closed. A timeout or
malformed/5xx provider response returns a safe `503` and leaves the attempt
`Initiated` so a later callback/reconciliation can still complete it. The 302
back to `FrontendResultBaseUrl` carries only route context, `outcome` and the
opaque token — no merchant id, amount, card PAN, provider payload or order totals.

**B043 interaction**: a cancel invalidates the order's still-`Initiated`
attempts to `Invalidated` (in the cancel's transaction, via
`completionService.InvalidateInitiatedAttemptsAsync`) so a late success can no
longer complete them; a late success for such an attempt therefore returns the
already-computed outcome — the order's current `Cancelled` status — and the
order never moves to `Paid`.

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

## 15. Public abuse controls (rate limiting and request-size bounds)

Introduced by B046. The four sensitive **anonymous** Shop flows are rate-limited
with named per-minute policies; the ZarinPal provider callback and all public
catalog reads are **not** limited (the callback is already bounded by its signed
state and many legitimate callbacks share one provider egress IP; catalog reads
are not a flood/ enumeration target in this task).

**Composition.** The host's one `AddRateLimiter` call is contributed by Shop
(`AddShopRateLimiter`), placed after both modules' registrations and before
`Build` in `Program.cs`. `UseForwardedHeaders` is the first middleware in the
pipeline (before `UseCors`) so the connection's remote IP — and therefore the
rate-limit partition key — reflects the value resolved through the
`ForwardedHeaders` trusted-proxy allowlist; `UseRateLimiter` and
`UseShopRequestBodySizeLimit` run before the module activation maps the
endpoints, so the `RequireRateLimiting` metadata is attached first.

**Policies and partition key** (`ShopRateLimitPolicies`, applied via
`RequireRateLimiting` on the mapped routes):

| Policy | Routes |
| --- | --- |
| `shop-order-lookup` | `POST /api/shop/{tenantId}/orders/lookup` |
| `shop-cart-mutation` | cart create / add item / update item / delete item |
| `shop-checkout-order` | `POST …/checkout/summary` and `POST /api/shop/{tenantId}/orders` |
| `shop-payment` | `POST …/orders/{orderId}/payments/initiate` |

Each policy is a per-key **fixed window** (one minute) with **queue length
exactly zero**: the bucket key is the normalized (lower-cased) route `tenantId`
plus the effective remote IP, so one tenant+IP pair has its own budget that
cannot drain or be drained by any other pair. `QueueLength` is refused at
startup if it is not `0` — an over-limit request is rejected immediately, never
queued or delayed.

**The 429.** Every over-limit request (valid or invalid input alike) receives
the one generic RFC 7807 `429`: `application/problem+json`, `type:
"shop_rate_limit"`, a generic Persian-safe detail that names no tenant, cart,
order, phone, tracking code, coupon or authority, an integer `Retry-After`
header of `1` and the matching integer `retryAfter` in the body. The policy name
and tenant ID are logged for operators; phone numbers, tracking codes, coupon
codes and the ZarinPal authority are never logged.

**Request-size bounds.** `UseShopRequestBodySizeLimit` (host-level, scoped to
Shop paths) rejects a Shop `Content-Length` above the bound with a generic
`413` (`type: "shop_request_too_large"`) before any model binding/validation
runs: a JSON body is capped at `MaxRequestBodyBytes` (Development default
512 KiB), anything else (e.g. the multipart media upload) at the 5 MiB hard
ceiling. The ZarinPal callback route separately rejects a query string above
16,000 characters with a generic `413` (`type: "shop_callback_too_large"`)
before the (untrusted) query is parsed.

## 16. Test map and commands

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
| `ShopPaymentLifecycleIntegrationTests.cs` | B044 payment lifecycle: valid initiation (sandbox provider, bank redirect, one `Initiated` attempt, only the token hash stored), same-key replay byte-identical with no duplicate attempt, two fresh keys → one live attempt, `AmountSnapshot` frozen before a later catalog price change, the 10-attempt cap (`409 too_many_payment_attempts`, 11th rejected), sandbox approve → `Paid`/`Succeeded`, sandbox decline → `PendingPayment`/`Failed` (`payment_declined`), duplicate success returns the stored outcome without re-applying, a late success for a `Cancelled` order returns the `Cancelled` outcome and never pays (cancel invalidated the attempt), only the SHA-256 of the token is stored (never the raw token), status endpoint: wrong token/wrong order/wrong tenant/no-attempts all return the one generic `404` (correct token `200`), sandbox resolve works in Development (approve + decline), Production + `Sandbox` provider fails closed at startup, the new migration applies cleanly on the existing history (adds exactly the six attempt columns + `shop_payment_initiations`), the redirect is the relative same-origin bank path, and `Idempotency-Key` missing/non-UUID → `400` naming the header — all asserted against real persisted rows |
| `ShopZarinPalPaymentIntegrationTests.cs` | B045 ZarinPal integration: request JSON/redirect URL/authority persistence, `IRT` and `IRR` amount conversion, verify code `100` success, code `101` matching prior success replay, code `101` mismatch fail-closed, `Status != OK` decline without verify, other verify codes fail, forged callback authority ignored in favor of stored authority/amount, tampered and expired signed state generic `404`, duplicate callback idempotency, provider timeout → `503` with attempt still `Initiated`, late success after cancel never pays, unsafe Production URL startup failure, no merchant/card-PAN leakage in response/logs, and `gateway_reference` vs `provider_reference` persistence |
| `ShopOrderLookupIntegrationTests.cs` | Guest lookup contract, non-leaking generic not-found |
| `ShopProfileIntegrationTests.cs` | B039 storefront profile: admin GET null-empty-state, create→update→GET round-trip with version bump, stale-version `409 stale_version`, concurrent first-create race (one `200`, one `409`, one row), tenant isolation (A cannot read/write B), `Shop.Settings.Manage` denial + role grant + Owner bypass, outside-trim / preserved-newlines / over-length validation, phone allowlist, Instagram URL rules (non-HTTPS / wrong host / look-alike domain), public `404` for missing/unpublished/malformed-id, public `200` shape with no `version`/`id`/`tenantId`/`updatedAtUtc`, and publication NOT gating the storefront catalog |
| `ShopCouponRulesIntegrationTests.cs` | B041 coupon rules: `ShopCouponPolicy` ordered reason codes (`coupon_inactive`/`_expired`/`_minimum_not_met`/`_limit_reached` + the caller's `coupon_not_found`) with the exact stable code surfaced in the `couponCode` field error, percentage discount capped at `maximumDiscountAmount` (and never past the subtotal), checkout summary never incrementing `RedeemedCount`, order creation incrementing by exactly one and storing the evaluated discount, a forced downstream DB failure rolling the redemption back, the concurrent last-redemption race (one 201 with the discount, one 400 `coupon_limit_reached`, total +1, loser's cart left active), stale `expectedVersion` → `409 stale_version`, `redemptionLimit` below `redeemedCount` → `400` with no persisted change, and tenant B unable to read/update/redeem tenant A's coupon with a byte-identical `coupon_not_found` |
| `ShopCouponRulesMigrationTests.cs` | B041 migration: a pre-`AddShopCouponRules` `shop_coupons` row backfills to `redemption_limit` NULL (unlimited), `redeemed_count` 0 and `version` 0, keeping its original fields so it stays usable |
| `ShopAdminOrdersIntegrationTests.cs` | B042 admin order reads: `Shop.Orders.View` matrix (Owner bypass 200, granted member 200, no-key member 403, `Shop.Orders.Manage`-only member still 403, anonymous 401), tenant isolation (foreign orders never listed, cross-tenant id → the byte-identical generic 404), list filters `q`/`status`/`fromUtc`/`toUtc` valid + invalid (400 naming the field), stable two-page pagination, item-snapshot fidelity after the product is renamed/repriced, the 20-newest payment-attempt cap (21 Failed attempt rows seeded directly — B044's API caps a live order at 10 attempts — → 20 returned, newest first), and the detail shape (customer, totals, items, `version: 1`) |
| `ShopOrderOperationsIntegrationTests.cs` | B043 order-status mutations (payments driven through B044's sandbox resolve): fulfil a `Paid` order (`Fulfilled`, `FulfilledAtUtc` set, stock untouched, version 1→2 — a payment does not bump the order version), cancel a `PendingPayment` order (stock restored to full initial, `InventoryReleasedAtUtc` set, the `Initiated` attempt invalidated to `Invalidated`, none `Failed`), a late success for a `Cancelled` order returns the already-computed `Cancelled` outcome (`200`) and never pays (the attempt stays `Invalidated`, none `Succeeded`), idempotent replay (same key + same action returns the byte-identical stored response, one operation row, stock restored once), same key + different action → `409 idempotency_key_conflict` with nothing performed, stale `expectedVersion` → `409 stale_version` with no change, two concurrent cancels (one `200`/one `409`, stock restored exactly once, one operation row), the `Shop.Orders.Manage` matrix (Owner 200, granted 200, no-key 403, `Shop.Orders.View`-only 403, anonymous 401), cross-tenant order id → the byte-identical generic 404, and validation `400`s (bad/missing action, non-UUID/missing `Idempotency-Key`) — all asserted against the real persisted rows, not just status codes |
| `ShopRateLimitIntegrationTests.cs` | B046 public abuse controls (dedicated DB, tiny per-minute limits so each fact fills a policy in a few requests): each of the four policies (`shop-order-lookup`/`shop-cart-mutation`/`shop-checkout-order`/`shop-payment`) admits its limit and returns the exact RFC 7807 `429 shop_rate_limit` (integer `Retry-After: 1` + body `retryAfter: 1`) on the next request; two tenant partitions on the same IP — exhausting one leaves the other's quota intact; a valid-lookup `429` and an invalid-lookup `429` are byte-identical (indistinguishable); an untrusted `X-Forwarded-For` is ignored and the direct remote IP is used for partitioning; Production with a missing `Shop:RateLimiting` value fails closed at startup; the rejection log line names the policy + tenant id but never the phone/tracking code; a normal (non-abuse) customer journey plus a catalog-read burst is not globally throttled; an oversized JSON Shop body → generic `413 shop_request_too_large` (no tenant leaked); an oversized ZarinPal callback query → generic `413 shop_callback_too_large` |

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

## 17. Current limitations

Verified against current code (not aspirational):

- **No `.Contract` project** — Shop's HTTP shapes live beside each feature.
  A second real .NET consumer of a Shop shape would trigger the same
  `module-contract-project` admission decision IAM's Contract project made.
- **Product media is local-disk only** — no cloud object storage, no CDN
  signing, no video, no image-cropping UI (B036's explicit non-goals).
- **No customer accounts** — every public/anonymous route is by design;
  there is no login, no saved address book, no order history beyond the
  guest tracking-code lookup.
- **Payments are request/verify only** — `Sandbox` and `ZarinPal` are registered behind the gateway-neutral seam, but refunds, provider inquiry/reconciliation scheduler, webhook handling, split payments, fees and multiple live merchant accounts are not implemented. See [Section 13](#13-payment-lifecycle-gateway-neutral).
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
- **No paid-order refund, returns or partial fulfilment/cancellation** — B043
  delivers the two operator transitions (`Paid→Fulfilled`,
   `PendingPayment→Cancelled`); there is no `Paid→Cancelled` path and no refund
   flow until its own slice.
- **Rate limiting is in-memory, per-process and per tenant+IP** — B046 adds the
   four anonymous-flow policies plus the request/callback-size bounds, but there
   is no distributed (Redis) counter, so each process keeps its own buckets; a
   horizontal scale-out would double the effective per-IP budget per node.
   Public catalog reads and the ZarinPal callback are not limited, and the
   limiter never queues (queue length is exactly zero — over-limit requests are
   rejected immediately). No CAPTCHA, WAF, bot scoring, caching or account
   lockout (all explicit non-goals).

## 18. Change-impact checklist

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
| payment gateway behavior | Payment lifecycle (gateway-neutral) |
| storefront profile/policy behavior | Storefront profile and policies |
| rate-limit / request-size behavior | Public abuse controls; Configuration; affected endpoint rows; composition/startup |
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

B044/S40 declaration: `SHOP.md impact: updated — Purpose (payment-lifecycle
wording), composition (gateway/resolver/completion-service registrations),
source map (ShopPaymentInitiation, Payment lifecycle row), Configuration
(Shop:Payments:Provider), Domain (ShopOrder.MarkPaid no version bump,
ShopPaymentAttempt B044 fields + Invalidated, new ShopPaymentInitiation row),
Persistence (AddShopPaymentLifecycle migration), Tenant/auth (anonymous route
wording), Endpoint catalog (39 routes; initiate/status/sandbox-resolve rows
replacing initiate/callback), Section 13 rewritten as the gateway-neutral
payment lifecycle, Test map (lifecycle class + migrated B042/B043 rows),
Current limitations (seam-in-place, sandbox-only).`
