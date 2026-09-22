# B040 — Cart reservation expiry

## 1. Files changed and why

- Shop cart domain, mapping and migration now store `Status`, `LastTouchedAtUtc`, `ExpiresAtUtc` and `ClosedAtUtc` so a cart can be active, converted or expired without deleting its history row.
- `ShopConfig` registers the injectable clock, expiry service and cleanup worker, and validates the cart lease settings at activation.
- Cart, checkout and order features now check the lease before using a cart; cart mutations extend the lease and order creation marks the cart converted.
- Integration tests cover lease extension, expiry, stock restoration, idempotency, batch cleanup, tenant isolation, expired-cart problem details and invalid configuration.
- `docs/modules/SHOP.md` and `docs/design/shop/http-contracts.md` record the delivered behavior for future backend and frontend tasks.

## 2. Request flow from endpoint to response

A shopper creates a cart with `POST /api/shop/{tenantId}/carts`. The server creates an active cart, computes `expiresAtUtc` from configuration and returns the cart id plus expiry time. Add/update/delete item requests first ensure the cart is still active, perform the stock reservation/release, extend the lease and return the full cart with the new expiry. A plain cart GET only verifies the lease and returns the cart; it does not refresh time.

Checkout summary and order creation call `EnsureActiveAsync` before pricing or snapshotting. If the cart is expired, the expiry transaction restores stock, removes cart items, marks the cart expired and the endpoint returns `410` with `type: shop_cart_expired`. If order creation wins, it snapshots the cart into an order, removes the cart items, marks the cart converted and returns the created order.

## 3. Backend concepts introduced

- `TimeProvider` is injected so time-dependent logic is not hard-coded to `DateTimeOffset.UtcNow`.
- A hosted `BackgroundService` can run periodic cleanup while the request path still performs the same safety check.
- A row lock (`FOR UPDATE`) serializes order creation and expiry against the same cart row.
- EF migrations can be hand-adjusted after generation to backfill existing data safely before making new columns required.

## 4. Important security decisions

- Lease length and expiry time are always server-owned; clients only receive `expiresAtUtc` and never submit it.
- Expired carts use one RFC 7807 problem type, `shop_cart_expired`, instead of leaking internal state.
- Tenant id remains in the first cart lookup predicate, and cleanup operates on cart ids already tied to their tenant.
- Startup fails closed for invalid cart lease configuration.

## 5. Alternatives deliberately postponed

- No distributed scheduler or durable job queue; the hosted worker is enough for this monolith slice.
- No signed-in cart merge or customer account ownership.
- No coupon redemption limits or order operations; later Shop slices own those.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test --filter FullyQualifiedName~ShopCartIntegrationTests
dotnet.exe test
```

Manual browser handoff for F058: create a cart, add an item and display the returned `expiresAtUtc`; then use an expired cart id and confirm cart/checkout/order flows show the expired-cart state for HTTP `410` / `type: shop_cart_expired`.

## 7. Review questions

1. Why must expiry and order creation lock the same cart row instead of only checking the cart status first?
2. Why does cart GET not extend the lease while add/update/delete do?
3. What would go wrong if the browser could submit its own `expiresAtUtc` or lease duration?
