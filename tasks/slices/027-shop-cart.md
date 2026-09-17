# S27 — Shop cart

## Outcome

A shopper browsing the storefront delivered in S26 can add a product
variant to a cart, see it, change its quantity, remove it and see a live
subtotal — all without signing in or creating any kind of account. The
cart's identity is the opaque TSID that is its own database primary key;
no separate token field or customer record exists.

darnoshop.com's own flow is guest-checkout-first: a shopper picks a color
and size, adds to cart, and only supplies contact/address details at
checkout (S28), never a login. This slice's cart screenshot evidence is
the same product-detail/cart screenshots cited in S26 (the "add to cart"
action) plus the implied cart-drawer/cart-page pattern every such
storefront needs before checkout — F032 is that page.

## New entities

| Entity | Fields |
| --- | --- |
| `ShopCart` | `Id` (Tsid — this literal id **is** the cart token returned to the browser), `TenantId` (Tsid), `CouponId` (Tsid?), `CreatedAtUtc` |
| `ShopCartItem` | `Id` (Tsid), `CartId` (Tsid), `ProductVariantId` (Tsid), `Quantity` (int), `UnitPriceSnapshot` (decimal) |

`UnitPriceSnapshot` is captured at add-to-cart time so a later admin price
edit (via B026) never retroactively changes the total of an already-open
cart — the same snapshot discipline `ShopOrderItem` will use in S29 for
the same reason, one step earlier in the lifecycle.

`ShopCart.CouponId` is nullable and added now (rather than only in S28)
because it is a plain column on the cart row itself, not a new table or a
speculative feature; it stays unused (never read or written) until B030
validates and applies a coupon in the next slice. This keeps `ShopCart`
from needing an additive schema change one slice later for a field that
belongs to the entity itself.

## Scope

**B028 — depends on B027:**
- `ShopCart`/`ShopCartItem` tables and their migration.
- `POST /api/shop/{tenantId}/carts` — create an empty cart, return its id.
- `POST /api/shop/{tenantId}/carts/{cartId}/items` — add an item; validates
  the chosen variant belongs to the same tenant, is active, and has enough
  live `StockQuantity` for the requested quantity (checked against the
  variant row read inside the same request, not a stale client-side
  number).
- `PATCH /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` — update
  quantity (same stock re-check).
- `DELETE /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` — remove.
- `GET /api/shop/{tenantId}/carts/{cartId}` — fetch the cart with its items
  and a computed subtotal (`sum(UnitPriceSnapshot * Quantity)`).
- Cart ownership is by opaque cart id only. Anyone who has the id can read
  or mutate that cart — this is the same trust model as darnoshop's own
  guest cart and is stated here as a deliberate, reviewed scope decision,
  not an oversight: there is no customer account anywhere in this module
  to own a cart more strongly, and the id itself (a 13-character
  Crockford-base32 TSID) is not realistically guessable or enumerable.

**F032 — depends on F030:**
- Cart page mock: item list (thumbnail, color/size label, quantity
  stepper, remove action), computed subtotal, "proceed to checkout"
  action, empty-cart state. Mocked data.

**F033 — depends on F032, B028:**
- Connect the cart page to B028's real API, including client-side
  persistence of the cart id across page loads. `src/web/src/features/auth/httpAuthAdapter.ts`
  and `AuthContext.tsx` already establish TenantForge's convention for
  holding a client-side credential: a small typed adapter around browser
  storage, JSON-serialized, read once at bootstrap. F033 follows that same
  adapter shape for the cart id, but deliberately in `localStorage`, not
  `sessionStorage`: the existing convention stores the *auth session* in
  `sessionStorage` because a session is meant to end when the tab closes;
  a shopping cart is the opposite — a shopper closing the tab and coming
  back tomorrow expects their cart to still be there. F033's Spec states
  this reasoning explicitly as the one deliberate deviation from the
  literal storage mechanism, while keeping the same adapter shape
  (dedicated module, typed get/set/remove, JSON-parsed, guarded by
  try/catch exactly like the design system's browser-storage guidance).

## Non-goals

- No customer account, login, or "my orders" list gated behind
  authentication anywhere in this module. Every Shop HTTP endpoint from
  B025 onward is deliberately anonymous — the only exception is the
  admin/authoring endpoints (B026, B029), which reuse IAM's existing
  tenant-membership authentication because they are back-office, not
  customer-facing. This guest-first design is a direct, reviewed choice
  matching darnoshop's own flow, not a placeholder for "add auth later."
- No cart expiry/cleanup job. An abandoned cart simply stays in the table;
  a cleanup task is out of scope until a real product need names it.
- No multi-cart-per-browser UI (one cart id at a time is tracked
  client-side).

## Verification

- `dotnet build TenantForge.sln --nologo` and the full integration suite
  pass after B028, including a live-stock race test (two concurrent
  add-item calls against a variant with `StockQuantity = 1` — exactly one
  must succeed).
- `npm run build` and `npm run lint` in `src/web/` pass after F032/F033.
- Real browser demo at 1440×900 and 390×844: add an item from the product
  detail page (F031's already-connected flow), open the cart, change
  quantity, remove an item, reload the page and confirm the cart id
  persists and the same cart reloads.
- No new browser console error.
