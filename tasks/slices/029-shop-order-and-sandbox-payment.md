# S29 — Shop order creation and sandbox payment

## Outcome

A shopper who has reached a valid checkout summary (S28) can turn it into
a real, persisted order — atomically, with no stock oversell possible even
under concurrent checkouts — and pay for it through a fake, in-app "bank
page" that stands in for a real Iranian payment gateway. The order review
screen shows the shopper what they are about to buy and pay for; the
sandbox bank page lets them approve or decline the payment, and the order
transitions to `Paid` or back to `PendingPayment`/`Failed` accordingly.

## New entities

| Entity | Fields |
| --- | --- |
| `ShopOrder` | `Id` (Tsid), `TenantId` (Tsid), `OrderNumber` (short human-facing string), `TrackingCode` (separate from `OrderNumber` — the secret-ish code a guest pairs with their phone number to look up the order later, S30), `Status` (enum: `PendingPayment`, `Paid`, `Cancelled`, `Fulfilled`), `CustomerName`, `CustomerPhone`, `ShippingProvince`, `ShippingCity`, `ShippingAddressLine`, `ShippingPostalCode`, `SubTotal`, `ShippingCost`, `DiscountAmount`, `GrandTotal`, `CreatedAtUtc` |
| `ShopOrderItem` | `Id` (Tsid), `OrderId` (Tsid), `ProductVariantId` (Tsid), `ProductNameSnapshot`, `VariantLabelSnapshot` (e.g. "قرمز / سایز M"), `UnitPrice` (decimal), `Quantity` (int) |
| `ShopPaymentAttempt` | `Id` (Tsid), `OrderId` (Tsid), `Provider` (string, e.g. `"Sandbox"`), `Status` (enum: `Initiated`, `Succeeded`, `Failed`), `GatewayReference` (string?), `CreatedAtUtc`, `CallbackReceivedAtUtc` (DateTimeOffset?) |

`ShopOrderItem` snapshots the product name and the exact variant label at
order-creation time (from the cart, which itself already snapshotted
`UnitPriceSnapshot` in S27) so a later product rename, re-color or price
edit in the admin catalog never rewrites a customer's order history —
the same reasoning `ShopCart`'s snapshot already established one step
earlier in the lifecycle.

## Payment abstraction

`IShopPaymentGateway`:

```csharp
Task<PaymentInitiation> InitiateAsync(ShopOrder order);
Task<PaymentVerification> VerifyCallbackAsync(/* provider-specific callback payload */);
```

`InitiateAsync` returns a redirect target (a URL the frontend navigates
to) and a gateway reference string persisted on the new
`ShopPaymentAttempt` row. `VerifyCallbackAsync` returns success/failure for
that attempt. Exactly one implementation exists in this whole module:
`SandboxPaymentGateway`, which "redirects" to an in-app fake bank page the
*frontend* renders (F038) with approve/decline buttons that call back into
the API (B032's callback endpoint) — there is no external HTTP call to any
real bank or payment provider anywhere in `SandboxPaymentGateway`.

This is a deliberate, named scope boundary, not an implicit TODO: **no
real ZarinPal integration, no ZarinPal credentials, no stored card data,
and no webhook signature scheme beyond whatever `SandboxPaymentGateway`
needs to demonstrate the `IShopPaymentGateway` seam exists and is real** (a
gateway reference plus a status transition is enough to prove the seam; it
does not need to prove production-grade webhook security for a provider
that is not actually being integrated yet). A later task swaps in a real
provider behind the same interface — that task is explicitly out of scope
for S29 and is not registered here.

## Route additions (reusing S26's convention)

- `POST /api/shop/{tenantId}/orders` (anonymous) — create the order from a
  cart id, address and coupon code (the same inputs B030 validated).
- `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate`
  (anonymous — the order id plus its `TrackingCode` is not required here
  since this call happens immediately after order creation, in the same
  browser flow that just created the order; B032's Spec fixes the exact
  authorization/lookup shape).
- `POST /api/shop/{tenantId}/orders/{orderId}/payments/callback`
  (anonymous — called by the frontend's fake bank page, carrying the
  gateway reference and the shopper's approve/decline choice).

## Scope

**B031 — depends on B030:**
- Order creation: given a cart id, address and coupon code (revalidated,
  not trusted from a client-supplied summary), atomically — inside one
  database transaction — snapshot the cart into a `ShopOrder`/
  `ShopOrderItem` set, generate `OrderNumber` and `TrackingCode`, and
  clear/close the cart (so it cannot be checked out again). Stock is
  **not** decremented again here: S27 already reserved it atomically at
  add-to-cart time (each `ShopCartItem` holds a live claim on
  `StockQuantity`), so order creation only consumes an already-reserved
  claim. The concurrency guard this slice must still prove is a
  double-order-creation race — two concurrent order-creation calls
  against the *same already-closed cart* — where the transaction (closing
  the cart is part of it) ensures exactly one succeeds and the other gets
  a clean "already checked out" response, never a duplicate order.

**B032 — depends on B031:**
- `IShopPaymentGateway` abstraction and `SandboxPaymentGateway`.
- Initiate endpoint: for a `PendingPayment` order, creates a
  `ShopPaymentAttempt` (`Status = Initiated`) and returns the sandbox
  redirect target.
- Callback endpoint: verifies the attempt and transitions the order to
  `Paid` (attempt `Succeeded`) or leaves/returns it to `PendingPayment`
  (attempt `Failed`) — never `Cancelled`/`Fulfilled` from this endpoint;
  those transitions belong to later, out-of-scope order-management work.

**F038 — depends on F037:**
- Order review/summary screen (mocked), the in-app fake "bank page"
  (approve/decline buttons) the Sandbox provider redirects to, and a
  payment-result screen. Mocked data.

**F039 — depends on F038, B031, B032:**
- Connect order review and the sandbox bank page to B031's and B032's real
  APIs.

## Non-goals

- No real ZarinPal (or any other real gateway) integration, credentials or
  webhook signature verification beyond the Sandbox seam described above.
- No stored card/payment-method data anywhere.
- No `Cancelled`/`Fulfilled` transition logic (no admin order-management
  screen) — those states exist on the enum because they are real order
  lifecycle states a later task will drive, but nothing in S29 sets them.
- No email/SMS order-confirmation notification.

## Verification

- `dotnet build TenantForge.sln --nologo` and the full integration suite
  pass after B031 and B032, including: a happy-path order creation, a
  double-order-creation race test (two concurrent order-creation calls for
  the same cart — exactly one must succeed, the other must fail cleanly,
  never a duplicate order or a double stock decrement), a sandbox-approve
  flow reaching
  `Paid`, and a sandbox-decline flow leaving the order in `PendingPayment`
  with a `Failed` payment attempt.
- `npm run build` and `npm run lint` in `src/web/` pass after F038/F039.
- Real browser demo at 1440×900 and 390×844: complete checkout into a real
  order, reach the fake bank page, approve it, and see the order marked
  paid; repeat and decline, and see the order stay unpaid with a clear
  retry path.
- No new browser console error.
