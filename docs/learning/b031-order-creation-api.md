# B031 — Order creation API

One backend slice: a single anonymous endpoint — `POST /api/shop/{tenantId}/orders` —
that turns a cart into a real, persisted order. It re-runs B030's
shipping-rate/coupon validation (a client-supplied summary is **never** trusted)
and then, inside **one database transaction**, snapshots the cart into a
`ShopOrder` plus one `ShopOrderItem` per line, generates a human `OrderNumber`
and a cryptographically-random `TrackingCode`, and consumes the cart. Two
honesty rules define the slice:

1. **Stock is not decremented a second time.** B028 already reserved each item's
   stock at add-to-cart time; order creation only finalizes that reservation by
   removing the cart rows.
2. **A cart can never become two orders.** Cart consumption happens inside the
   same transaction as the order insert, so of two concurrent "place order"
   calls for one cart, exactly one wins (`201`) and the other gets a clean `404`
   — the database can never hold a duplicate order for the same cart.

## 1. Files changed and why

| File | Why |
| --- | --- |
| `domain/ShopOrder.cs` | New order aggregate: tenant, the two identifiers, `Status` (`PendingPayment`/`Paid`/`Cancelled`/`Fulfilled` — only the first two are ever set in S29), customer + shipping fields, the four money fields (`GrandTotal` computed as `subTotal − discountAmount + shippingCost`), `CreatedAtUtc`. Factory `Create(...)` + `MarkPaid`/`MarkPaymentFailed` for B032. |
| `domain/ShopOrderItem.cs` | One row per cart line. **Snapshots** the product name and the exact variant label (`"Black / M"`) at order time, so a later admin rename/re-color/price edit can never rewrite a customer's order history — the same snapshot reasoning `ShopCart` already established. |
| `infrastructure/ShopOrderMap.cs` | `shop_orders` table map. Two **unique** indexes (`order_number`, `tracking_code`) make the uniqueness promises durable in the database, not just in code; the composite `(tenant_id, tracking_code, customer_phone)` index is the lookup B033 (guest order lookup) will serve. |
| `infrastructure/ShopOrderItemMap.cs` | `shop_order_items` map, cascade FK to the order (delete the order → its lines go too). |
| `infrastructure/ShopDbContext.cs` | +2 `DbSet`s (`Orders`, `OrderItems`), +2 `ApplyConfiguration` lines. |
| `infrastructure/Migrations/20260917202519_AddShopOrders.*` | Generated migration (created with the Spec's command **plus `--context ShopDbContext`** — required because the API host exposes two DbContexts). |
| `features/orders/OrderContracts.cs` | `CreateOrderRequest` (the same fields B030 priced, plus `customerName`/`customerPhone`) and `OrderCreatedResponse` (ids, codes, status, the four totals). |
| `features/orders/OrderCreationFeature.cs` | The one endpoint (see §2) + the two generators (`GenerateUniqueOrderNumberAsync`, `GenerateTrackingCode`). |
| `ShopModule.cs` | +1 using + one `endpoints.MapOrderCreationFeature();` inside the existing `MapShopModule` seam. No new service registration. |
| `tests/.../IamDbFixture.cs` | `ShopOrderDbFixture` / `ShopOrderIsolatedCollection` on a dedicated DB (`tenantforge_shop_order_tests`) — the every-test-class-owns-a-database convention. |
| `tests/.../ShopOrderIntegrationTests.cs` | 9 facts (see §6). |
| `tests/.../ShopModuleIntegrationTests.cs` | Its own docstring mandates: *"every task that adds a Shop table must add it here in the same change."* `ExpectedShopTables` gains `shop_orders` + `shop_order_items` (and the file's pre-mangled docstring was repaired). Not a weakened test — the exact-table-set contract genuinely grew. |

## 2. Request flow (the single endpoint)

`POST /api/shop/{tenantId}/orders`

1. `TsidId.TryParse` on route `tenantId` and body `cartId`; either failing →
   `404` (same "not a cart I know" convention as every Shop anonymous route).
2. Field validation starts here (collected, not thrown): blank
   `customerName`/`customerPhone` → field-keyed errors.
3. **`BeginTransactionAsync()`** — from this point everything is all-or-nothing.
4. **Cart consumption is read first, tracked**:
   `db.CartItems.Where(...).ToListAsync()` (tracked, not `AsNoTracking()`) +
   the tenant-scoped `Carts.AnyAsync` check. No cart or zero items →
   `RollbackAsync` + `404`. Reading the rows *inside* the transaction is what
   makes the double-order guard work (§3).
5. `subTotal` = `Sum(item.UnitPriceSnapshot * item.Quantity)` over the cart
   items (the cart's own snapshots — never a live re-price).
6. Province + coupon: the **identical** validation block B030 uses —
   `shippingProvince` required / tenant-scoped rate lookup / "does not ship to
   the selected province", and the normalized-code coupon lookup with
   active/expiry checks and the same percentage/fixed discount math. Any error
   → `RollbackAsync` + one `400` `ValidationProblem`.
7. `GenerateUniqueOrderNumberAsync` — `ORD-{yyMMdd}-{4 random digits}`,
   uniqueness-checked against `shop_orders` up to 5 times (then a hard 500,
   which is correct: a 1-in-10,000-per-day collision repeated five times is a
   real incident, not a client error). `GenerateTrackingCode()` — 12 chars from
   `RandomNumberGenerator` over the no-`0/O/1/I` alphabet.
8. `ShopOrder.Create(...)` + one `ShopOrderItem.Create(...)` per cart line
   (name + variant label snapshotted from the product/variant rows).
9. `db.CartItems.RemoveRange(cartItems)` — the consumption. **No touch to
   `StockQuantity`.**
10. `SaveChangesAsync()` → `CommitAsync()` → `201` with
    `Location: /api/shop/{tenantId}/orders/{orderId}` and the response body.

### The race, precisely

Two calls for the same cart both pass step 4 (each reads the same cart row
inside its own uncommitted transaction). Both build an order. The first
commits. The second's `SaveChangesAsync` then issues a
`DELETE FROM shop_cart_items WHERE id = …` that matches **0 rows** — the winner
already deleted them — so EF throws `DbUpdateConcurrencyException`. The
handler catches exactly that exception, returns `404`, and the uncommitted
transaction rolls back automatically on disposal. That catch is the **one
deliberate addition to the Spec's verbatim feature code**: the Spec's
acceptance criteria require the loser to get a clean `404` ("exactly one
succeeds (201), the other gets 404"), and without the catch the loser is a
raw 500. The database-level outcome (exactly one order) held either way; the
catch only changes the HTTP answer.

## 3. Backend concepts introduced

- **Transactional consumption as an idempotency guard.** "Delete the source
  rows, then insert the result, inside one transaction" is the simplest correct
  answer to "one cart, one order": the source's disappearance *is* the lock.
  The `DbUpdateConcurrencyException` on the loser's delete is EF's way of
  saying "the world changed between your read and your write" — and here it is
  a business answer (`404`), not a bug.
- **Snapshot semantics.** The order rows copy `UnitPriceSnapshot` (already a
  snapshot from B028) plus the product name and variant label. Order history
  is now immutable with respect to catalog edits — the third snapshot in the
  lifecycle (variant price at cart time → cart line → order line).
- **Two identifiers, two jobs.** `OrderNumber` is human-facing and
  predictable in shape (`ORD-…`); `TrackingCode` is secret-ish and
  unguessable (CSPRNG, restricted alphabet). B033 will pair `TrackingCode`
  with the customer's phone for guest lookup — which is why the tracking code
  is never derived from anything the shopper already has.
- **Unique indexes as the durable contract.** The code *tries* to generate
  unique order numbers, but the unique indexes are what make a duplicate
  impossible even if the retry loop is bypassed — code promises, constraints
  enforce.
- **Raw SQL from tests, honestly.** Shop's types are `internal` and only IAM
  has `InternalsVisibleTo` for the test assembly, so "exactly one order was
  persisted" is asserted with `select count(*) from shop_orders where
  tenant_id = @tenantId` — the same pattern `ShopModuleIntegrationTests` uses
  for the table set. (Counts are tenant-scoped because xunit runs the class's
  facts in parallel on the shared collection database; the acceptance
  criterion is "one order *for that cart*", and each fact's cart belongs to a
  freshly created tenant.)

## 4. Important security decisions

- **Anonymous, isolated by the route.** No `.RequireAuthorization()`; every
  query is scoped by the route `tenantId` (cart existence, rate, coupon,
  order insert). Another tenant's cart id is a `404`, full stop.
- **Server-side re-validation.** The endpoint re-prices from the cart's own
  rows and re-checks the rate/coupon; nothing the client sent is believed. A
  tampered "summary" cannot change what is charged.
- **Tracking code is a capability secret-in-waiting.** 12 chars × 32-symbol
  alphabet ≈ 58 bits of entropy, from `System.Security.Cryptography`, with
  ambiguous characters removed so a guest can type it back unambiguously. It
  is never derived from `OrderNumber`, the cart id, or the phone number.
- **No secrets or PII logged.** The customer name/phone live only in the
  response and the order rows; nothing is written to the log.
- **Fail-closed money paths.** Unshippable province and invalid coupon are
  `400`s that roll the transaction back — no order, no partial state.

## 5. Alternatives deliberately postponed

- **`ShopCart.CouponId` is still not written.** B028/B030 left it for "when a
  cart becomes an order"; this Spec's verbatim feature code does not set it,
  so it stays `null`. Setting it would be a one-line nicety, not this slice.
- **Optimistic concurrency tokens / row-version locks** on the cart: the
  transaction + consumption guard already gives exactly-one-winner; a
  rowversion column buys nothing for a single-row deletion.
- **Idempotency keys** (client-supplied request id for safe retries): the
  cart-consumption 404 already makes a retried request safe *and* informative;
  a header-level key would add protocol surface the demo does not need.
- **Payment initiation, email/SMS, admin order management, `Cancelled`/
  `Fulfilled` transitions, stock decrement** — all explicitly out of scope
  (B032 and later slices own them).

## 6. How to verify

```bash
# automated
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~ShopOrderIntegrationTests"   # 9/9
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo   # full suite
```

Manual (same setup as B030's demo: Postgres via Docker, API on
`http://0.0.0.0:5080`, WSL2 DB host `172.31.123.207`, curl through the WSL
gateway `172.31.112.1`): author a tenant + 890,000 product + Tehran rate 50,000
+ `WELCOME10` (10 %) + a 1-item cart, then:

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders \
  -H "Content-Type: application/json" \
  -d '{
        "cartId": "<cartId>",
        "customerName": "مریم رضایی",
        "customerPhone": "09121234567",
        "shippingProvince": "Tehran",
        "shippingCity": "Tehran",
        "shippingAddressLine": "خیابان ولیعصر",
        "shippingPostalCode": "1234567890",
        "couponCode": "WELCOME10"
      }'
# → 201 {"orderId":"…","orderNumber":"ORD-260917-3293","trackingCode":"MWGUXZSKAJGZ",
#        "status":"PendingPayment","subTotal":890000.00,"discountAmount":89000.00,
#        "shippingCost":50000.00,"grandTotal":851000.00}   ← the Spec's worked example
```

Immediately repeat the exact same call → `404`. `GET` the cart → empty items.
The variant's `stockQuantity` reads the same value before and after the order
(it dropped once at add-to-cart time, never a second time).

Observed in this delivery: all four of the above, exactly.

## 7. Three questions for the learner

1. The loser of the double-order race gets a `404` produced from a caught
   `DbUpdateConcurrencyException` at save time, while a serially-repeated call
   gets a `404` from the empty-cart check at read time. **Why are both answers
   "404 — cart not found" rather than, say, a `409 Conflict "already ordered"`?**
   What would a client have to rely on to tell the two apart, and is that
   reliable enough to design on?
2. `TrackingCode` is random and `OrderNumber` is pattern-based. If B033's guest
   lookup accepts **either** identifier, which one becomes the real secret, and
   what does the composite index `(tenant_id, tracking_code, customer_phone)`
   tell you about how B033's endpoint must be shaped (and why the phone is in
   the index but not in the tracking code)?
3. Order rows snapshot the product name and variant label. If an admin renames
   a product *and* changes a variant's color label tomorrow, what does an
   existing order's item line say, and which test in
   `ShopOrderIntegrationTests` would you extend to lock that behavior down?
