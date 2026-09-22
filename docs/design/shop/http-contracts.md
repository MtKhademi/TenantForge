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

Existing POST/list/deactivate remain. New
`PUT /api/tenants/{tenantId}/shop/coupons/{couponId}` returns
`200 CouponResponse`. Checkout/order validation uses:
`coupon_not_found`, `coupon_inactive`, `coupon_expired`,
`coupon_minimum_not_met`, `coupon_limit_reached`.

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
type AdminOrderDetailResponse = {
  id: string
  orderNumber: string
  trackingCode: string
  status: AdminOrderStatus
  customer: AdminOrderCustomerResponse
  totals: AdminOrderTotalsResponse
  items: AdminOrderItemResponse[]
  paymentAttempts: AdminPaymentAttemptResponse[]
  version: number
  createdAtUtc: string
}
```

| Method | Route | Success |
|---|---|---|
| GET | `/api/tenants/{tenantId}/shop/orders?pageNumber=&pageSize=&status=&q=&fromUtc=&toUtc=` | `200 AdminOrderListResponse` |
| GET | `/api/tenants/{tenantId}/shop/orders/{orderId}` | `200 AdminOrderDetailResponse` |

Both require `Shop.Orders.View`.

## S39 / B043 — order status actions

```ts
type ChangeOrderStatusRequest = {
  action: 'Fulfill' | 'Cancel'
  expectedVersion: number
}
```

`PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status` requires
`Shop.Orders.Manage` and an `Idempotency-Key` UUID header, then returns the
updated `AdminOrderDetailResponse`. Stale or invalid transition is `409`.

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
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/initiate` | `Idempotency-Key`; `200 InitiatePaymentResponse` |
| GET | `/api/shop/{tenantId}/orders/{orderId}/payments/status?token={resultToken}` | `200 PaymentStatusResponse` |
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve` | Development only; `ResolveSandboxPaymentRequest`; `200 PaymentStatusResponse` |

Unknown token/order pairs use one generic `404`.

## S41 / B045 — ZarinPal callback

The browser does not call the provider callback itself.

`GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`
validates signed state, verifies with ZarinPal when appropriate, and responds
with `302` to the configured frontend result route carrying only the opaque
result token/order route context. Merchant ID, amount and provider response
payload are never browser-authored.

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
