---
id: B031
slice: S29
title: Order creation API
agent: backend-mentor
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Add `POST /api/shop/{tenantId}/orders`: given the same inputs B030's
checkout summary validated (cart id, address, optional coupon code),
atomically snapshot the cart into a `ShopOrder`/`ShopOrderItem` set,
decrement each variant's stock, generate an `OrderNumber` and a
`TrackingCode`, and clear the cart — all inside one transaction so a stock
race can never oversell.

# Context

Read `tasks/slices/029-shop-order-and-sandbox-payment.md` completely.
Read the already-delivered `features/checkout/CheckoutFeature.cs` (B030)
— this task re-runs the same coupon/shipping validation rather than
trusting a client-supplied summary (the summary is a preview; nothing
about it is treated as authoritative when the order is actually created,
since the cart, coupon or shipping rate could have changed in the
meantime).

# Scope

`features/orders/OrderCreationFeature.cs`:

`POST /api/shop/{tenantId}/orders` — anonymous. Body: same shape as
B030's summary request (`cartId`, shipping address fields, `couponCode`).

Inside one database transaction:

1. Re-validate the coupon and shipping province exactly as B030 does
   (reject with the same distinct errors on failure — do not create a
   partial order).
2. Re-load the cart's items with a row-level lock/guarded update on each
   variant's `StockQuantity` (the same concurrency-safe pattern B028's
   add-item endpoint uses), decrementing each variant by its cart
   quantity. If any variant no longer has enough stock, roll back the
   entire transaction and return a clear "no longer available" error
   naming which item — never a partial order with some items missing.
3. Create the `ShopOrder` row: `Status = PendingPayment`, the four money
   fields from the (re-validated) summary computation, the shipping
   address fields, `CustomerName`/`CustomerPhone` from the request body
   (add these two fields to the request shape; B030's summary request did
   not need them since it created nothing), a freshly generated
   `OrderNumber` (short, human-facing, e.g. a zero-padded sequential or
   date-based string — pick one deterministic, collision-checked scheme
   and document it in the implementation) and `TrackingCode` (a separate,
   random, sufficiently long string — not derived from `OrderNumber` or
   any other guessable value, since B033/S30 relies on it not being
   guessable together with a phone number).
4. Create one `ShopOrderItem` per cart item, copying
   `ProductNameSnapshot` and `VariantLabelSnapshot` (e.g. "قرمز / سایز M")
   from the current product/variant rows (read once, inside the same
   transaction, before the cart is cleared) and `UnitPrice`/`Quantity`
   from the cart item.
5. Delete the cart's items (or mark the cart consumed — pick whichever is
   simpler given `ShopDbContext`'s existing cascade-delete configuration;
   either way, a `GET` on the same cart id after order creation must no
   longer return purchasable items) so the same cart cannot be checked out
   twice.
6. Commit. Return the created order's `OrderNumber`, `TrackingCode`, and
   totals.

# Non-goals

- No payment initiation (B032).
- No email/SMS confirmation.
- No admin order-management endpoint (list/view orders as a tenant admin)
  — out of scope for this task; nothing in S29's registered tasks needs
  it yet.

# Acceptance

- A happy-path order creation returns `OrderNumber`/`TrackingCode` and
  correctly decremented stock on every involved variant.
- A stock-race integration test: two orders created concurrently from two
  different carts that both want the last unit of one variant — exactly
  one order succeeds; the other gets a clear "no longer available" error
  and no partial `ShopOrder`/`ShopOrderItem` rows are left behind for it.
- An invalid coupon or unshippable province at order-creation time is
  rejected with the same distinct errors B030 uses.
- After a successful order creation, `GET`ting the original cart no longer
  shows purchasable items (it is consumed).
- `TrackingCode` is never derived from `OrderNumber`, the cart id, or any
  other value guessable from information the shopper already has.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the happy path, the stock-race case, coupon/
shipping re-validation failures, and cart consumption. The full existing
IAM suite continues to pass unmodified.

Manual:

- Build a cart, get a checkout summary, then create the order with the
  same inputs and confirm the returned totals match the summary and the
  cart is now consumed.

# Lifecycle

Add row `B031` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B030`, and Spec link
`tasks/backend/B031-order-creation-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
