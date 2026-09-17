---
id: B030
slice: S28
title: Checkout API
agent: backend-mentor
source: tasks/slices/028-shop-checkout.md
---

# Objective

Add `POST /api/shop/{tenantId}/checkout/summary`: given a cart id, a
shipping address and an optional coupon code, validate the coupon and the
shipping province and return a priced checkout summary — without creating
an order or any other persisted record.

# Context

Read `tasks/slices/028-shop-checkout.md` completely, in particular the
explicit rule that an unshippable province must produce a plain, distinct
"not shippable here" response, never a silent zero shipping cost. Read the
already-delivered `features/carts/CartsFeature.cs` (B028) for how to load
a cart and compute its subtotal, and `features/coupons/CouponsFeature.cs`/
`features/shipping/ShippingRatesFeature.cs` (B029) for the entities this
task reads.

# Scope

`features/checkout/CheckoutFeature.cs`:

`POST /api/shop/{tenantId}/checkout/summary` — anonymous. Body:
`cartId`, `shippingProvince`, `shippingCity`, `shippingAddressLine`,
`shippingPostalCode`, `couponCode` (nullable).

1. Load the cart (`404` if it does not exist for this tenant, or is
   empty — an empty cart cannot be checked out).
2. Compute `subTotal` from the cart's items (same computation as B028's
   `GET` cart endpoint).
3. Resolve `shippingProvince` against `ShopShippingRate` for this tenant.
   No matching row → return a distinct, clearly-named validation problem
   (for example `{ "shippingProvince": ["This tenant does not ship to the
   selected province."] }`) — never `200` with `shippingCost: 0`.
4. If `couponCode` is supplied, resolve it against `ShopCoupon` for this
   tenant (case-insensitive). Missing, inactive, or `ExpiresAtUtc` in the
   past → a distinct validation problem naming the coupon as invalid
   (never silently ignored — a shopper who typed a real-looking code
   deserves to know it did not apply). When valid, compute
   `discountAmount` from `DiscountType`/`DiscountValue` against
   `subTotal` (a `Percentage` coupon never discounts below zero; a
   `FixedAmount` coupon is capped at `subTotal` so the total can never go
   negative).
5. Return `{ subTotal, discountAmount, shippingCost, grandTotal }` where
   `grandTotal = subTotal - discountAmount + shippingCost`. Nothing is
   persisted — no `ShopOrder`, no mutation of the cart's `CouponId`
   column (that column is written only in B031, at order-creation time).

# Non-goals

- No order/payment creation (B031/B032).
- No coupon usage-count or minimum-order-value rule beyond active/expiry.

# Acceptance

- A valid cart, shippable province and no coupon returns the correct
  `subTotal`/`shippingCost`/`grandTotal` with `discountAmount = 0`.
- A valid coupon reduces `grandTotal` by the correct amount for both
  `Percentage` and `FixedAmount` types.
- An expired or inactive coupon code returns a clear validation error, not
  a summary that silently ignores it.
- An unshippable province returns a clear validation error, never a `200`
  with a zero shipping cost.
- An empty or nonexistent cart returns `404`/a clear error, never a
  summary with a zero subtotal that looks like a valid empty order.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: valid summary without a coupon, valid summary
with a percentage coupon, valid summary with a fixed-amount coupon capped
at the subtotal, expired-coupon rejection, inactive-coupon rejection,
unshippable-province rejection, and empty-cart rejection. The full
existing IAM suite continues to pass unmodified.

Manual:

- Build a cart via B028's API, call the summary endpoint with a shippable
  province and a valid coupon, and confirm the totals; repeat with an
  unshippable province and confirm the distinct error.

# Lifecycle

Add row `B030` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependencies `B028, B029`, and Spec link
`tasks/backend/B030-checkout-summary-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
