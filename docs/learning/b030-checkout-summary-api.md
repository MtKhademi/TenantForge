# B030 — Checkout API

One backend slice: a single anonymous, **read/compute-only** endpoint —
`POST /api/shop/{tenantId}/checkout/summary` — that prices a cart against the
tenant's configured per-province shipping rate (B029) and an optional coupon
(B029), and **persists nothing**: no order, no checkout state. It reuses only
what already exists. The one rule it must honor is honesty: a province with no
configured rate returns a plain "not shippable here" `400`, never a `200` with
a silently-wrong zero shipping cost; an empty cart returns `404`, never a
zero-subtotal summary that looks like a valid empty order.

## 1. Files changed and why

| File | Why |
| --- | --- |
| `features/checkout/CheckoutContracts.cs` | `CheckoutSummaryRequest` (cartId + address + optional coupon) and `CheckoutSummaryResponse` (the four totals). City / address line / postal code are **accepted but not priced** in S28 — shipping is a flat per-province cost, so only the province drives pricing. Kept in the request so the address form (F036/F037) has a home without a second round-trip. |
| `features/checkout/CheckoutFeature.cs` | The one endpoint. Anonymous (no `.RequireAuthorization()`) — every B027/B028 storefront route is anonymous; isolation is enforced by scoping **every query on the route `tenantId`**, never by a credential. |
| `ShopModule.cs` | +1 using + one `endpoints.MapCheckoutFeature();` inside the existing `MapShopModule` seam. No new service registration: the endpoint needs only the already-registered `ShopDbContext`. |
| `tests/.../IamDbFixture.cs` | `ShopCheckoutDbFixture` / `ShopCheckoutIsolatedCollection` on a dedicated DB (`tenantforge_shop_checkout_tests`) — the every-test-class-owns-a-database convention, kept off the other Shop databases so their row-count assertions stay stable. |
| `tests/.../ShopCheckoutIntegrationTests.cs` | 9 facts (see §6). Authors catalog/rate/coupon data through B026/B029's **authenticated** admin APIs and the cart through B028's **anonymous** API, then drives checkout with a bare client that sends no `Authorization` header — proving the route is genuinely anonymous while tenant isolation still holds server-side. |

No domain type, no table, no migration, no `ShopDbContext` change. `ShopCart.CouponId` (added in B028, "set in B030") is **still not written** — the Spec's summary is compute-only and stores nothing; that column stays populated by B031 when a cart actually becomes an order.

## 2. Request flow (the single endpoint)

`POST /api/shop/{tenantId}/checkout/summary`

1. `TsidId.TryParse` on the route `tenantId` and the body `cartId`; either
   failing → `404` (a malformed id is never a `400` or `500` — it is simply
   "not a cart I know").
2. Cart existence, **scoped by tenant**:
   `db.Carts.AnyAsync(c => c.Id == cartTsid && c.TenantId == tenantTsid)` →
   no match → `404`. This is the whole of cross-tenant isolation on an
   anonymous route.
3. Subtotal in the database, the same way B028 computes it:
   `SumAsync(item => item.UnitPriceSnapshot * item.Quantity)` over the cart's
   items. `subTotal <= 0` (an empty cart) → `404`.
4. Province: blank → error `shippingProvince: "required"`; else a
   tenant-scoped `SingleOrDefaultAsync` on `ProvinceName` (trimmed). No row →
   `shippingProvince: "This tenant does not ship to the selected province."`;
   a row → `shippingCost = rate.Cost`.
5. Coupon (only when a code was sent): normalize with
   `Trim().ToUpperInvariant()` and look up `NormalizedCode` (tenant-scoped).
   Missing / `!IsActive` / past `ExpiresAtUtc` → `couponCode: "This coupon code
   is not valid."`; otherwise `Percentage` → `Math.Round(subTotal * value / 100m, 2)`,
   `FixedAmount` → `Math.Min(value, subTotal)`.
6. Any errors collected → one `Results.ValidationProblem(errors)` (`400`,
   Problem+JSON, field-keyed). None → `200` with
   `{ subTotal, discountAmount, shippingCost, grandTotal }` where
   `grandTotal = subTotal - discountAmount + shippingCost`.

Note the ordering: the `404`/empty-cart checks run **before** validation, so a
missing cart is never answered with a pricing error, and a valid cart's
province/coupon problems are all reported in a single `400` rather than the
first one winning.

## 3. Backend concepts introduced

- **Compute-only endpoint.** The handler reads three tables and writes none.
  There is no service layer or domain method because the "logic" is a single
  request-priced calculation; introducing one would be abstraction for a
  hypothetical future (B031's order creation is a different, stateful flow and
  gets its own code).
- **Validation vs not-found.** A cart that does not exist (or is empty) is a
  `404` — a resource problem. A province we don't serve or a dead coupon is a
  `400` — the request is well-formed but cannot be priced. Conflating the two
  (e.g. returning `200` with `shippingCost: 0`) is the exact failure the
  source slice forbids.
- **Tenant isolation on an anonymous route.** There is no JWT, so isolation
  cannot come from an authenticated principal. It comes from the fact that the
  only tenant the endpoint ever writes into a query is the parsed route
  segment — the integration fact proves a cart under tenant B is a `404` under
  tenant A's route.
- **Collect-all validation.** Errors from province *and* coupon are gathered
  into one dictionary and returned together (the B026/B029 pattern), so a
  shopper correcting both a province and a coupon fixes both in one edit.

## 4. Security decisions

- **Anonymous by design, isolated by query scope.** No credential, no
  `.RequireAuthorization()`. Every DB query filters on the route `tenantId`,
  so one shopper can never price another tenant's cart, rate or coupon — the
  test asserts this explicitly.
- **No enumeration.** Unknown *and* malformed cart/tenant ids both answer the
  same bare `404`; the response never distinguishes "no such cart" from
  "wrong tenant" from "not a TSID".
- **Honest totals, not optimistic ones.** An unshippable province and a
  dead coupon are loud `400`s, never silently folded into a total the shopper
  would trust. `grandTotal` can never be computed from a zero shipping cost
  that the tenant never configured.
- **No secrets or tokens in logs; no echoed input.** Validation text is fixed
  strings naming the field only.
- **Read/compute-only means no race to defend.** Because nothing is written,
  there is no reservation to make atomic (that is B028's job on cart-add and
  B031's job at order-creation); two simultaneous summaries may both read the
  same stock and both succeed, which is correct for a *preview*.

## 5. Alternatives deliberately postponed

- **Order creation / payment (B031, B032).** The summary previews; it never
  commits. Snapshotting the cart, decrementing stock, generating an
  `OrderNumber`/`TrackingCode` and clearing the cart is the next slice.
- **Coupon usage-count / minimum-order-value rules.** B029's `ShopCoupon`
  carries only `IsActive` + `ExpiresAtUtc` by design; a rule engine is not
  built speculatively.
- **A `checkout` domain service or `CheckoutCalculator`.** One request, one
  calculation — a dedicated type would be structure without a second caller.
  It earns its keep only when B031 needs the *same* pricing to commit an order.
- **Weight/size shipping tiers, courier selection.** S28's non-goal: one flat
  per-province cost, which is all darnoshop's own checkout needs.
- **A `docs/modules/Shop.md` handbook / `Shop.Contract` project.** Same
  admission rule as B026–B029: not yet earned; these contracts stay
  module-local.

## 6. Verify it

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
# focused:
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~ShopCheckoutIntegrationTests"
```

The 9 facts: valid summary with no coupon (`discountAmount = 0`); percentage
coupon (lowercase input, still matches — the lookup runs on the normalized
code); fixed-amount coupon capped at the subtotal; expired coupon → `400`;
deactivated coupon → `400`; unshippable province → `400` (never a `200` with
zero shipping); missing province → `400`; empty cart → `404`; and
nonexistent / malformed / cross-tenant cart + malformed tenant → all `404`.

Manual (Development, cross-OS: the API is a Windows `dotnet` process, Postgres
is a WSL2 container — point the two DB connection strings at WSL2's eth0 IP,
not `localhost`):

```bash
# as a tenant owner (B026/B029 admin APIs):
curl -X POST .../api/tenants/<tenantId>/shop/shipping-rates \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"provinceName":"Tehran","cost":50000}'                       # 200
curl -X POST .../api/tenants/<tenantId>/shop/coupons \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"code":"WELCOME10","discountType":"Percentage","discountValue":10}'   # 201
# author one product (variant price 890000) via B026, then ANONYMOUSLY:
curl -X POST .../api/shop/<tenantId>/carts                          # 201 {"cartId":...}
curl -X POST .../api/shop/<tenantId>/carts/<cartId>/items \
  -H "Content-Type: application/json" -d '{"productVariantId":"<variantId>","quantity":1}'   # 200
curl -X POST .../api/shop/<tenantId>/checkout/summary \
  -H "Content-Type: application/json" \
  -d '{"cartId":"<cartId>","shippingProvince":"Tehran","couponCode":"WELCOME10"}'
#  -> 200 {"subTotal":890000,"discountAmount":89000,"shippingCost":50000,"grandTotal":851000}
# same call with "shippingProvince":"Khorasan" (no rate)  ->  400
#   {"errors":{"shippingProvince":["This tenant does not ship to the selected province."]}}
```

## 7. Three review questions

1. The endpoint computes the subtotal with `SumAsync` in the database and the
   discount in .NET. Why is that split reasonable here, and at what point would
   the whole pricing computation need to move into one place (e.g. a method
   B031 also calls)? What would break if B031 re-implemented the same math
   instead of reusing it?
2. An unshippable province is a `400` and an empty cart is a `404`. Is that
   distinction stable as this becomes a real checkout, or would a real shopper
   flow prefer a single "cannot proceed" body? What does the source slice's
   "say it plainly" rule actually forbid — and what would it allow?
3. This route is anonymous, yet no shopper can price another tenant's cart.
   Trace exactly which line makes that true, and explain why that guarantee
   would *not* hold if the handler had queried the cart by `cartId` alone
   without the `tenantId` filter. What would a cross-tenant probe look like to
   an attacker in that case?
