# S28 — Shop checkout: shipping, coupons, address

## Outcome

Before an order exists, a shopper can see an honest, priced checkout
summary: their cart's subtotal, any discount their coupon code earns,
the shipping cost for the province they typed, and the grand total — or,
if the tenant does not ship to that province at all, a plain statement of
that fact instead of a silently-wrong zero. A boutique owner can configure
which Iranian provinces they ship to (and at what cost) and can create,
list and deactivate coupon codes, from the same admin area B026 already
established.

## New entities

| Entity | Fields |
| --- | --- |
| `ShopCoupon` | `Id` (Tsid), `TenantId` (Tsid), `Code`, `DiscountType` (enum: `Percentage`, `FixedAmount`), `DiscountValue` (decimal), `IsActive` (bool), `ExpiresAtUtc` (DateTimeOffset?) |
| `ShopShippingRate` | `Id` (Tsid), `TenantId` (Tsid), `ProvinceName`, `Cost` (decimal) |

`ShopShippingRate` is one row per Iranian province the tenant actually
ships to. A province with no row is not shippable there — the checkout API
must say that plainly (a distinct, named error, not `Cost = 0`), matching
the fact that a real boutique often only ships within a handful of
provinces near its warehouse or via a specific courier's coverage area,
exactly as darnoshop's own checkout flow is province-driven.

## Route additions (reusing S26's convention)

- Admin (authenticated): `/api/tenants/{tenantId}/shop/shipping-rates`
  (list/set) and `/api/tenants/{tenantId}/shop/coupons` (create/list/
  deactivate).
- Public (anonymous): `/api/shop/{tenantId}/checkout/summary` — accepts a
  cart id, an address, and an optional coupon code; returns a priced
  summary without creating anything. Checkout summary is intentionally a
  read/compute-only endpoint, not the order-creation endpoint — that is
  B031/S29's job, kept separate so a shopper can preview totals (and
  correct a mistyped coupon or an unshippable province) before committing.

## Scope

**B029 — depends on B026 (same admin area/permission pattern):**
- Tenant-scoped admin endpoints to set/list per-province shipping rates
  and to create/list/deactivate coupons. Reuses the same tenant-membership
  authorization B026 already established — no new permission concept.

**B030 — depends on B028, B029:**
- `POST /api/shop/{tenantId}/checkout/summary`: given a cart id, an
  address (province, city, address line, postal code) and an optional
  coupon code, validates the coupon (exists for this tenant, `IsActive`,
  not past `ExpiresAtUtc`) and the shipping province (must have a
  configured `ShopShippingRate` row or the response says plainly the
  tenant does not ship there — a distinct problem detail, not a 200 with a
  zero cost), and returns `{ subtotal, discountAmount, shippingCost,
  grandTotal }`. Creates nothing — no order, no persisted checkout state.

**F034 — depends on F029:**
- Admin shipping-rate and coupon management mock (province/cost rows list
  and create; coupon list, create, deactivate action). Mocked data.

**F035 — depends on F034, B029:**
- Connect F034 to B029's real API.

**F036 — depends on F033:**
- Checkout page mock: address form (province, city, address line, postal
  code), coupon-code field, a live order summary (subtotal, discount,
  shipping, grand total) that recomputes as the shopper edits the form.
  Mocked data.

**F037 — depends on F036, B030:**
- Connect the checkout page to B030's real API.

## Non-goals

- No order is created anywhere in this slice — that is B031/S29.
- No payment of any kind is initiated here.
- No shipping-rate table beyond the flat per-province cost described
  above (no weight/size-based shipping tiers, no courier selection UI) —
  that is exactly and only what darnoshop's own checkout needs.
- No coupon usage-count limit, per-customer limit, or minimum-order-value
  rule. `ShopCoupon` carries only what B030 actually validates
  (`IsActive`, `ExpiresAtUtc`); a richer coupon-rule engine is not built
  speculatively.

## Verification

- `dotnet build TenantForge.sln --nologo` and the full integration suite
  pass after B029 and B030, including: a valid-coupon summary, an
  expired/inactive-coupon summary (discount not applied, or a clear
  validation error — B030's Spec fixes which), and an unshippable-province
  summary (a distinct error, never a silent zero shipping cost).
- `npm run build` and `npm run lint` in `src/web/` pass after F034–F037.
- Real browser demo at 1440×900 and 390×844: as an admin, add a shipping
  rate and a coupon; as a shopper, reach checkout, enter an address in a
  shippable province with a valid coupon and see the correct totals, then
  try an unshippable province and see the plain "not shippable here"
  message instead of a wrong total.
- No new browser console error.
