# Shop planned HTTP contracts — S32–S42

This is the persistent handoff between the mock-first frontend tasks and the
backend tasks. A live B-task Spec is authoritative while planned. Before that
B-task is delivered and its executable Spec is deleted, the backend task must
update its section here to match the delivered C# records, route metadata,
nullability, enum strings and problem codes. The delivered code and integration
tests win if this document ever drifts.

ASP.NET Core serializes C# record properties as camelCase JSON. Every ID is a
canonical 13-character TSID string and every timestamp is ISO-8601 UTC.

## Common shapes

```ts
type PaginationMetadata = {
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

type ShopProblem = {
  status: number
  type?: string
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}
```

Malformed/foreign resource IDs use the same non-leaking not-found response.
Admin routes use the current bearer token and server-side Shop permissions.

## S32 / B036 — product media

Existing `GET /api/tenants/{tenantId}/shop/products/{productId}` extends its
product response with:

```ts
type ProductImageResponse = {
  id: string
  altText: string
  displayOrder: number
  width: number
  height: number
  contentUrl: string
}

type ProductGalleryResponse = {
  images: ProductImageResponse[]
  galleryVersion: number
}

type ProductResponseWithGallery = ExistingProductResponse & ProductGalleryResponse
type ReorderProductImagesRequest = {
  imageIds: string[]
  expectedGalleryVersion: number
}
```

| Method | Route | Request | Success |
|---|---|---|---|
| POST | `/api/tenants/{tenantId}/shop/products/{productId}/images` | multipart `file`, `altText`, `expectedGalleryVersion` | `201 ProductGalleryResponse` |
| PUT | `/api/tenants/{tenantId}/shop/products/{productId}/images/order` | `ReorderProductImagesRequest` | `200 ProductGalleryResponse` |
| DELETE | `/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}?expectedGalleryVersion={n}` | none | `204` |
| GET | `/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content` | none | protected image bytes |
| GET | `/api/shop/{tenantId}/media/{imageId}` | none | public active image bytes |

Errors: field validation `400`, forbidden `403`, generic missing `404`, stale
gallery `409`, oversized `413`, unsupported/corrupt media `415`.

## S33 / B037 — storefront discovery

```ts
type DiscoverySort = 'newest' | 'price-asc' | 'price-desc' | 'name'
type StorefrontProductSummaryResponse = {
  id: string
  name: string
  slug: string
  effectivePrice: number
  compareAtPrice: number | null
  isOnSale: boolean
  isSoldOut: boolean
  thumbnailUrl: string | null
  images: ProductImageResponse[]
}
type StorefrontProductListResponse = {
  products: StorefrontProductSummaryResponse[]
  pagination: PaginationMetadata
}
```

`GET /api/shop/{tenantId}/products?pageNumber=1&pageSize=24&q=&categorySlug=&sort=newest&saleOnly=false`
returns the paginated list of a tenant's active products in active categories.
Query parameters: `q` (name search, trimmed and truncated to 100 characters,
case-insensitive), `categorySlug` (active same-tenant category), `sort` (one of
`newest`, `price-asc`, `price-desc`, `name`; missing/blank means `newest`), and
`saleOnly` (`true` keeps only products where `compareAtPrice` exceeds the
displayed price).

Card price and sale semantics: `effectivePrice` is the lowest
`PriceOverride ?? BasePrice` among the product's in-stock variants; when no
variant is in stock the product is `isSoldOut: true` and `effectivePrice` falls
back to `basePrice`. `isOnSale` is `compareAtPrice > effectivePrice`. Sold-out
products always list last, then by the chosen sort, then by product id.
`thumbnailUrl` is the first ordered image's public byte URL (or `null`). SKU and
raw stock counts are never exposed.

`GET /api/shop/{tenantId}/categories/{categorySlug}/products` returns the same
`StorefrontProductListResponse` shape; on that route `effectivePrice` is the
product `basePrice` and `isSoldOut`/`isOnSale`/`thumbnailUrl` are derived the
same way.

Errors: invalid query values return validation `400` (keyed by field, `sort` for
an unknown value); a malformed `tenantId` or a named category that is
absent/inactive in this tenant returns a generic `404`.

## S34 / B038 — category hierarchy

Admin category contracts gain one member, `parentCategoryId: string | null`
(a canonical 13-char TSID string; `null` = root):

```ts
type CreateCategoryRequest = {
  name: string
  slug: string
  displayOrder: number
  parentCategoryId?: string | null
}
type UpdateCategoryRequest = {
  name: string
  slug: string
  displayOrder: number
  isActive: boolean
  parentCategoryId?: string | null
}
type CategoryResponse = {
  id: string
  tenantId: string
  name: string
  slug: string
  displayOrder: number
  isActive: boolean
  parentCategoryId: string | null
}
```

A supplied `parentCategoryId` must name an **active root** category of the
same tenant (a parent that already has a parent is not a root). Every
violation — missing, malformed, other tenant, inactive, not a root, or the
category's own id — returns `400` with the RFC 7807 field error
`errors.parentCategoryId: ["Select an active root category."]`. Maximum depth
is root + one direct child. A category that already has children can never be
re-parented under another category: that update returns `409 Conflict`
(`title: "Reparent conflict"`) and changes nothing. A root may be deactivated
while its children remain stored.

The public category list now nests one level:

```ts
type StorefrontCategoryResponse = {
  id: string
  name: string
  slug: string
  displayOrder: number
  children: StorefrontCategoryResponse[]
}
type StorefrontCategoryListResponse = {
  categories: StorefrontCategoryResponse[]
}
```

`categories` contains **roots only**, each ordered by `displayOrder` then id,
with its direct children in the same order under `children` (`[]` for a root
with none; children never carry their own `children` — always `[]`). A
category is public only while it **and** its root are active ("effective
activity"); a deactivated root removes the whole group from this list.

No new route. Product filtering by category slug now resolves the hierarchy:
a **root** slug's `GET /api/shop/{tenantId}/categories/{categorySlug}/products`
(and the `categorySlug` filter on `GET /api/shop/{tenantId}/products`) returns
products assigned to the root **plus** products assigned to its direct
children; a **child** slug returns only that child's products. Product detail
(`GET /api/shop/{tenantId}/products/{productSlug}`) and the public media-bytes
route (`GET /api/shop/{tenantId}/media/{imageId}`) additionally return `404`
when the owning category is not effectively active (pre-B038, detail ignored
category state entirely).

## S35 / B039 — profile and policies

```ts
type ShopProfileDto = {
  id: string
  tenantId: string
  name: string
  tagline: string
  supportPhone: string
  instagramUrl: string | null
  aboutText: string
  shippingPolicy: string
  paymentPolicy: string
  returnPolicy: string
  privacyPolicy: string
  isPublished: boolean
  version: number
  updatedAtUtc: string
}
type ShopProfileResponse = { profile: ShopProfileDto | null }
type PublicShopProfileResponse = Omit<ShopProfileDto, 'id' | 'tenantId' | 'version' | 'updatedAtUtc'>
type SaveShopProfileRequest = Omit<ShopProfileDto, 'id' | 'tenantId' | 'version' | 'updatedAtUtc'> & {
  expectedVersion: number | null
}
```

| Method | Route | Auth | Success |
|---|---|---|---|
| GET | `/api/tenants/{tenantId}/shop/profile` | JWT + membership | `200 ShopProfileResponse` (`profile: null` before the first save) |
| PUT | same | JWT + `Shop.Settings.Manage` | `200 ShopProfileResponse` |
| GET | `/api/shop/{tenantId}/profile` | anonymous | `200 PublicShopProfileResponse`; unpublished/missing is `404` |

Validation: field validation `400` (`name`/`tagline`/`supportPhone` required;
over-length `name`(100)/`tagline`(180)/`supportPhone`(30)/`aboutText`(4000)/
policy fields(6000); `supportPhone` allows only digits, spaces, `+`, `-`,
`(`, `)`; `instagramUrl` must be HTTPS on `instagram.com` or a
`*.instagram.com` subdomain). Permission denied `403`. A stale create or
update (mismatched or missing `expectedVersion`, or the losing side of a
concurrent first-create) is `409` with RFC 7807 `type: "stale_version"`. The
tenant is always taken from the authenticated route context, never the body.

## S36 / B040 — cart reservation lease

Existing create/read/mutation responses add server-owned expiry:

```ts
type CreateCartResponse = { cartId: string; expiresAtUtc: string }
type CartResponse = {
  cartId: string
  items: ExistingCartItemResponse[]
  subTotal: number
  expiresAtUtc: string
}
```

Existing cart mutation responses and cart read responses include `expiresAtUtc`.
Successful add/update/delete item mutations extend the lease; `GET` cart does
not. Checkout summary and order creation first verify the cart lease. Existing
cart, checkout-summary and order-creation routes return RFC 7807 status `410`
with `type: 'shop_cart_expired'` when the lease was released. Successful order
creation marks the cart converted; converted carts are no longer readable as an
active cart. No request accepts a client expiry value.

## S37 / B041 — coupon rules

```ts
type CouponResponse = {
  id: string
  code: string
  discountType: 'Percentage' | 'FixedAmount'
  discountValue: number
  minimumSubtotal: number
  maximumDiscountAmount: number | null
  redemptionLimit: number | null
  redeemedCount: number
  isActive: boolean
  expiresAtUtc: string | null
  version: number
}
type CreateCouponRequest = {
  code: string
  discountType: 'Percentage' | 'FixedAmount'
  discountValue: number
  minimumSubtotal: number
  maximumDiscountAmount: number | null
  redemptionLimit: number | null
  expiresAtUtc: string | null
}
type UpdateCouponRequest = {
  discountValue: number
  minimumSubtotal: number
  maximumDiscountAmount: number | null
  redemptionLimit: number | null
  expiresAtUtc: string | null
  isActive: boolean
  expectedVersion: number
}
```

Existing POST/list/deactivate remain, with `CouponResponse` gaining
`minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`, `redeemedCount`
and `version` (so `POST …/coupons`, `GET …/coupons` list items and
`PATCH …/deactivate` all return the extended shape). New
`PUT /api/tenants/{tenantId}/shop/coupons/{couponId}` (protected by
`Shop.Shipping.Manage`) returns `200 CouponResponse` on success, `409`
Problem Details with `type: 'stale_version'` when `expectedVersion` does not
match the stored `version`, and `400` Problem Details naming `redemptionLimit`
when the new limit is set below the current `redeemedCount`. The request carries
neither `code` nor `discountType`, so the code and discount type cannot change
through the endpoint.

Validation rules (centralized in `ShopCouponPolicy`, applied at checkout
preview and re-applied at order consumption): `minimumSubtotal` ≥ 0 and
`maximumDiscountAmount` ≥ 0 when set; a set `redemptionLimit` is `1..1000000`;
percentage `discountValue` stays `1..100` and fixed stays `> 0`; the computed
discount is capped at `maximumDiscountAmount` (when set) and never exceeds the
subtotal. `checkout/summary` is preview-only — it never increments
`redeemedCount`. `orders` re-validates under a coupon row lock and increments
`redeemedCount` exactly once on success.

A rejected coupon on the anonymous `checkout/summary` or `orders` routes is a
`400` Problem Details with a `couponCode` field error naming the exact stable
code in parentheses — one of `coupon_not_found`, `coupon_inactive`,
`coupon_expired`, `coupon_minimum_not_met`, `coupon_limit_reached`.
`coupon_not_found` is returned identically for a code that does not exist and
one that belongs to another tenant (non-leaking).

## S38 / B042 — admin order reads

```ts
type AdminOrderStatus = 'PendingPayment' | 'Paid' | 'Cancelled' | 'Fulfilled'
type AdminOrderSummaryResponse = {
  id: string
  orderNumber: string
  customerName: string
  customerPhone: string
  status: AdminOrderStatus
  grandTotal: number
  createdAtUtc: string
}
type AdminOrderListResponse = {
  orders: AdminOrderSummaryResponse[]
  pagination: PaginationMetadata
}
type AdminOrderCustomerResponse = {
  name: string
  phone: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
}
type AdminOrderTotalsResponse = {
  subTotal: number
  shippingCost: number
  discountAmount: number
  grandTotal: number
}
type AdminPaymentAttemptResponse = {
  id: string
  status: string
  createdAtUtc: string
}
type AdminOrderDetailResponse = {
  id: string
  orderNumber: string
  trackingCode: string
  status: AdminOrderStatus
  customer: AdminOrderCustomerResponse
  totals: AdminOrderTotalsResponse
  items: OrderLookupItemResponse[]  // reused from S30 guest lookup
  paymentAttempts: AdminPaymentAttemptResponse[]
  version: number
  createdAtUtc: string
}
```

| Method | Route | Success |
|---|---|---|
| GET | `/api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | `200 AdminOrderListResponse` |
| GET | `/api/tenants/{tenantId}/shop/orders/{orderId}` | `200 AdminOrderDetailResponse` |

**Auth**: both require a valid JWT **and** `Shop.Orders.View`. Anonymous → `401`;
authenticated member without the key → `403`. Tenant Owner bypasses the key
check. `Shop.Orders.Manage` is a separate key enforced only on the S39
order-status route — it does **not** unlock these read-only routes.

**List filters** (all optional, combined with AND):

| Param | Constraint | Failure |
|---|---|---|
| `q` | trimmed, ≤ 100 chars; case-insensitive contains on order number, tracking code, customer phone, customer name (OR) | `400` `q` field error |
| `status` | must be one of the defined `AdminOrderStatus` values (case-sensitive) | `400` `status` field error |
| `fromUtc` | parseable date-time, inclusive start | `400` `fromUtc` field error |
| `toUtc` | parseable date-time, exclusive end; range ≤ 366 days from `fromUtc` | `400` `toUtc` field error |

**Sort**: always `CreatedAtUtc desc, Id desc` — stable across pages.

**Detail 404**: a malformed order ID, a well-formed ID belonging to a different
tenant, or a non-existent order ID all return the **same** RFC 7807 `404`
after the authorization check runs — the body does not reveal which case was
hit. The anonymous tracking-code lookup path is not reused.

**Payment attempts**: capped at the **20 newest** per order (descending by
`CreatedAtUtc`, then `Id`). This cap is temporary until the payment-lifecycle
task (B044) enforces a smaller, lifecycle-aware bound.

**Snapshot fidelity**: item values (`ProductNameSnapshot`, `VariantLabelSnapshot`,
`UnitPrice`) come from the order's frozen snapshot at creation time — never a
live join to the current product name or price.

## S39 / B043 — order status actions

```ts
type ChangeOrderStatusRequest = {
  action: 'Fulfill' | 'Cancel'
  expectedVersion: number
}
```

| Method | Route | Request/success |
|---|---|---|
| PATCH | `/api/tenants/{tenantId}/shop/orders/{orderId}/status` | `ChangeOrderStatusRequest` + `Idempotency-Key` header; `200 AdminOrderDetailResponse` |

**Request**: `action` must be exactly `Fulfill` or `Cancel` (absent or any other
value → `400` naming `action`); `expectedVersion` is the optimistic-concurrency
token the client last saw for the order's `version` (must be `≥ 1`). The
`Idempotency-Key` header is required and must be a UUID (missing or non-UUID →
`400` naming `Idempotency-Key`).

**Auth**: a valid JWT **and** `Shop.Orders.Manage` (the key B042 registered but
left reserved — this route is the one that enforces it). Anonymous → `401`; an
authenticated member without the key → `403`; a tenant Owner bypasses the key
check. Malformed, cross-tenant and missing order ids return the same generic
`404` after authorization (S38's non-leaking behavior).

**Transitions** (exactly these — no other move is legal, and there is no
transition out of either terminal state):

- `Fulfill`: `Paid → Fulfilled` — sets `FulfilledAtUtc`, bumps `version`. Never
  touches stock or inventory.
- `Cancel`: `PendingPayment → Cancelled` — sets `CancelledAtUtc`, bumps
  `version`, and in the same transaction restores each order item's quantity to
  its variant (exactly once, stamped by `InventoryReleasedAtUtc`) and invalidates
  every still-`Initiated` payment attempt. No order history row is deleted.
- Any other requested transition (including `Paid → Cancelled`, or anything from
  `Fulfilled`/`Cancelled`) → `409` with RFC 7807 `type: "invalid_order_transition"`.
- A valid transition whose `expectedVersion` no longer matches the stored
  `version` → `409` with RFC 7807 `type: "stale_version"`; nothing changes.

**Idempotency**: the first successful call persists one operation row keyed by
the request's `Idempotency-Key` (unique per tenant) holding the response it
returned. Re-sending the **same key with the same action** returns that stored
response again (`200`, byte-identical) without re-running the transition.
Re-sending the **same key with a different action** is a `409` with
`type: "idempotency_key_conflict"` — neither action is performed.

**Late payments**: a payment success that arrives after an order is `Cancelled`
never moves it to `Paid`. Because S40 (B044) makes `Cancel` invalidate the
order's still-`Initiated` attempts in the same transaction, the late success
arrives at an already-resolved attempt and is answered with the already-computed
outcome — the order's current `Cancelled` status (`200`, S40's status shape).
(Pre-S40 the legacy callback route answered this `409`; that route is deleted.)

**F061 handoff**: the route, request, response (`AdminOrderDetailResponse`,
S38), error codes (`invalid_order_transition`, `stale_version`,
`idempotency_key_conflict`), and permission key are exactly as above. To
exercise it end to end, seed a tenant + product variant, create an order through
the anonymous flow, then either pay it (to fulfil) or cancel it straight from
`PendingPayment`; send a fresh UUID `Idempotency-Key` per logical operation and
the order's current `version` as `expectedVersion`.

## S40 / B044 — gateway-neutral payment lifecycle

```ts
type PaymentProvider = 'Sandbox' | 'ZarinPal'
type InitiatePaymentResponse = {
  provider: PaymentProvider
  redirectUrl: string
  resultToken: string
}
type PaymentStatusResponse = {
  orderNumber: string
  status: 'PendingPayment' | 'Paid' | 'Cancelled' | 'Fulfilled'
  providerReference: string | null
}
type ResolveSandboxPaymentRequest = {
  authority: string
  approved: boolean
}
```

| Method | Route | Request/success |
|---|---|---|
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/initiate` | `Idempotency-Key` header; `200 InitiatePaymentResponse` |
| GET | `/api/shop/{tenantId}/orders/{orderId}/payments/status?token={resultToken}` | `200 PaymentStatusResponse` |
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve` | Development only; `ResolveSandboxPaymentRequest`; `200 PaymentStatusResponse` |

The legacy `POST …/payments/callback` route (a browser-authored `approved`
boolean) is **deleted**.

**Initiate**: anonymous, no body — everything is server-derived. The
`Idempotency-Key` header is required and must be a UUID (missing or non-UUID →
`400` naming `Idempotency-Key`). Only a `PendingPayment` order may start an
attempt (any other status → `409`); an order has at most one live `Initiated`
attempt (a fresh key for an order that has one returns that attempt's stored
response instead of minting a second row); an order has at most **10** attempts
in total (the 11th → `409` with RFC 7807 `type: "too_many_payment_attempts"`).
Re-sending the **same key with the same canonical request** replays the stored
response byte-identically; the **same key with a different request** is a `409`
with `type: "idempotency_key_conflict"`. `redirectUrl` is the gateway's target
(Sandbox: the relative same-origin path
`/shop/{tenantId}/bank?authority={authority}`; a real provider: an absolute
URL). `resultToken` is the raw callback token, returned exactly once per
initiation (and re-derived, byte-identically, on a same-key replay); only its
SHA-256 hash is ever stored.

**Status**: anonymous. The `token` query value is the raw callback token,
compared to the stored hash in **fixed time**. It returns only
`orderNumber`/`status`/`providerReference` — nothing else about the order.
Every miss — missing/blank token, wrong token, wrong order, wrong tenant, an
order with no attempts — returns the one identical generic `404`.

**Sandbox resolve**: mapped **only in Development** (`ResolveSandboxPaymentRequest`;
blank `authority` → `400` naming `authority`). It is the only browser-driven
payment simulation. A success for an attempt whose order is no longer payable
answers `409`; a success for an already-resolved (e.g. cancel-invalidated)
attempt returns the already-computed outcome — the order's current status —
without re-applying side effects.

**Provider configuration** (`Shop:Payments:Provider`): exactly `Sandbox` or
`ZarinPal`. Outside Development the value is required and `Sandbox` is refused
— a Production host with the sandbox provider **fails closed at startup**
rather than silently allowing the browser-driven simulation. A configured
provider with no registered gateway (ZarinPal before B045) fails closed at the
first resolution.

**F062 handoff**: the three routes, the response shapes, the `Idempotency-Key`
header (a fresh UUID per logical initiation, replayable on retry), the
`resultToken` (store it; it is the only credential for the status lookup and is
returned only once), the error codes (`too_many_payment_attempts`,
`idempotency_key_conflict`), and the sandbox `redirectUrl` (treat a leading-slash
path as same-origin) are exactly as above. To exercise the sandbox end to end:
initiate (send an `Idempotency-Key`), navigate to `redirectUrl`, approve or
decline on the in-app bank page, which POSTs to sandbox resolve; afterwards the
status lookup with the `resultToken` reports the order's current status.

## S41 / B045 — ZarinPal callback

The browser does not verify payment itself and never authors success, amount,
merchant id, order id or provider reference.

`GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`
validates signed state (tenant id, order id, attempt id, raw result token;
30-minute lifetime). `Status != OK` resolves the stored attempt as declined and
**does not** call ZarinPal verify. `Status == OK` verifies server-to-server with
the stored authority and frozen amount. Verification code `100` succeeds; code
`101` succeeds only as a duplicate when its `RefId` matches a success reference
already stored for the same attempt; every other code fails closed. Timeout/5xx
or malformed provider replies return a safe `503` and leave the attempt
`Initiated`.

Success and declined terminal outcomes respond with `302` to
`{FrontendResultBaseUrl}/shop/{tenantId}/payment-result?outcome=approved|declined&token={resultToken}`.
The redirect carries only route context, outcome and the opaque result token;
merchant id, amount, card PAN and provider response payload are never echoed to
the browser.

## S42 / B046 — rate-limit problem

Sensitive endpoints may return:

```ts
type RateLimitedProblem = ShopProblem & {
  status: 429
  type: 'shop_rate_limit'
  retryAfterSeconds: number
}
```

The response also carries integer `Retry-After`. The same public shape is used
whether a lookup/cart/order input exists or not.

## Change gate

Every B036–B046 task must compare delivered routes and C# records with its
section before review. Update this document in the same task when any member,
nullability, enum, error code or route changed. Otherwise put this exact form in
self-review and the PR body:

`Shop HTTP contract docs impact: none — <specific reason>`

Every F044–F063 task reads the relevant section. A mock task builds schemas from
it; a connection task additionally verifies it against delivered C# code and
integration tests before binding HTTP.
