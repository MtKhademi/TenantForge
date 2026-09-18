# B032 — Sandbox payment API

## 1. Files changed and why

| File | Why |
| --- | --- |
| `src/modules/shop/.../domain/ShopPaymentAttempt.cs` | New entity: one row per payment attempt (`Initiated → Succeeded/Failed`). `TryResolve` encodes the resolve-once rule in the domain, not in the endpoint. |
| `src/modules/shop/.../infrastructure/ShopPaymentAttemptMap.cs` | Maps to `shop_payment_attempts`: unique index on `gateway_reference` (a reference identifies exactly one attempt), non-unique index on `order_id`, cascade FK to `shop_orders`. |
| `src/modules/shop/.../infrastructure/ShopDbContext.cs` | +`PaymentAttempts` DbSet and `ApplyConfiguration(new ShopPaymentAttemptMap())`. |
| `infrastructure/Migrations/20260918072959_AddShopPaymentAttempts.cs` (+Designer, snapshot) | Generated EF migration for the new table (generated files, separate from authored changes). |
| `features/payments/IShopPaymentGateway.cs` | The seam: `InitiateAsync(order)` / `VerifyCallbackAsync(reference, approved)` plus the two result records. |
| `features/payments/SandboxPaymentGateway.cs` | The one real implementation. Minting a 32-char CSPRNG hex `gatewayReference`, persisting the attempt, and building the in-app redirect URL. No outbound HTTP anywhere. |
| `features/payments/PaymentContracts.cs` | Public HTTP records: `InitiatePaymentResponse`, `PaymentCallbackRequest`, `PaymentCallbackResponse`. |
| `features/payments/PaymentsFeature.cs` | The two anonymous endpoints and the mapping from gateway outcome → order transition / HTTP status. |
| `ShopConfig.cs` | `AddScoped<IShopPaymentGateway, SandboxPaymentGateway>()` — scoped because the gateway holds the scoped `ShopDbContext`. |
| `ShopModule.cs` | `endpoints.MapPaymentsFeature();` in the activation phase, alongside the other `Map*Feature` calls. |
| `tests/.../IamDbFixture.cs` | `ShopPaymentDbFixture` (dedicated `tenantforge_shop_payment_tests` DB) + collection, matching the per-task-database pattern. |
| `tests/.../ShopPaymentIntegrationTests.cs` | 7 facts: all five acceptance criteria plus the 404/400 family, with raw-SQL assertions on the attempt rows. |
| `tests/.../ShopModuleIntegrationTests.cs` | `shop_payment_attempts` added to `ExpectedShopTables` (that test's own docstring mandates it per new table). |

## 2. Request flow

**Initiate** `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate`
1. Parse tenant/order TSID strings → `404` on malformed.
2. Load the order scoped by both ids → `404` if absent (covers cross-tenant too).
3. Status gate: not `PendingPayment` → `409` "Order is not awaiting payment".
4. `gateway.InitiateAsync`: CSPRNG `gatewayReference` (`RandomNumberGenerator.GetBytes(16)` → lowercase hex), insert `ShopPaymentAttempt(Initiated)`, `SaveChanges`, return `(reference, "/shop/{tenantId}/payments/sandbox/{reference}")`.
5. `200` with `{gatewayReference, redirectUrl}`. The order is still `PendingPayment` — initiating is not paying.

**Callback** `POST .../payments/callback` `{gatewayReference, approved}`
1. Parse ids → `404`; blank `gatewayReference` → `400` naming the field.
2. Load order (scoped) → `404`; not `PendingPayment` → `409` "Order already resolved".
3. `gateway.VerifyCallbackAsync`: find the attempt by reference, `TryResolve(approved)` — returns `false` when unknown or already resolved; only a real resolution calls `SaveChanges`.
4. Map the outcome:
   - succeeded → `order.MarkPaid()`, save, `200 {orderId, "Paid"}`;
   - declined and the attempt exists → `order.MarkPaymentFailed()`, save, `200 {orderId, "PendingPayment"}`;
   - declined but no attempt → `404`;
   - approved but unverifiable (unknown/duplicate reference) → `409` "Payment callback rejected".

`MarkPaymentFailed` is a no-op unless the order is `PendingPayment` — a decline can never move a `Paid` order backwards.

## 3. Backend concepts introduced

- **Strategy seam (`IShopPaymentGateway`)**: the endpoints depend on an interface, not an implementation. A later task can swap in a real gateway (e.g. ZarinPal) behind the same two methods without touching routing or the order lifecycle. DI resolution is scoped (`ShopConfig`), matching how the gateway holds the scoped `ShopDbContext`.
- **Two-phase external operation**: initiate (client-side redirect) and callback (server-side verification) are separate endpoints because a real bank redirects the shopper away; the callback is the only place truth is recorded.
- **Resolve-once state machine**: `TryResolve` returns `false` when the attempt is not `Initiated`, so a duplicate/late callback can never rewrite a resolved attempt — enforced in the domain, observed by the endpoint via the return value plus the attempt row (`AnyAsync` distinguishes "declined" from "unknown reference").
- **Status transitions stay in the order aggregate**: the endpoint only calls `ShopOrder.MarkPaid`/`MarkPaymentFailed`; nothing in S29 sets `Cancelled`/`Fulfilled`.
- **Unique index as a concurrency guard**: `gateway_reference` is unique, so two initiates can never collide and a callback can only ever match exactly one attempt.
- **CSPRNG for identifiers**: the 32-hex gateway reference (like B031's tracking code) is cryptographically random and never derived from anything the client already knows.

## 4. Important security decisions

- **Anonymous by design, guarded by unguessability + scoping**: like B028–B031's shopper endpoints, these are public routes; the order is fetched scoped by `{tenantId}` **and** `{orderId}`, so a wrong-tenant or guessed-but-real id is a clean `404`, never data leakage or a status change.
- **Default-deny transitions**: only `PendingPayment` orders accept payment actions; every other state → `409`. A duplicate callback is rejected (`409`), not silently re-applied — no double-"paid" flip, no second `CallbackReceivedAtUtc`.
- **The gateway is a sandbox by explicit scope boundary**: no real provider call, no credentials, no stored card data, no webhook signature scheme. The class's XML doc and the slice document name this as a deliberate boundary, not a TODO.
- **No secrets logged or echoed**; the only identifiers in responses are the TSID order id, the enum status, and the gateway reference the client already holds.

## 5. Alternatives deliberately postponed

- **Real gateway + webhook HMAC verification** — belongs to the task that actually integrates a provider; the interface shape was chosen so it slots in without endpoint changes.
- **Idempotency keys / retry handling for the callback** — the non-goals limit this to "reject when not `PendingPayment`" + "reject a re-resolved attempt".
- **`Cancelled`/`Fulfilled` transitions and admin order management** — later order-management work, out of S29.
- **A payment-attempt API for the shopper** (e.g. "my last attempt") — no current frontend consumer; not introduced.
- **Merging the two endpoints into one** — a real gateway redirects the browser away, so initiate/callback must be separate HTTP operations.

## 6. How to verify

```bash
# build + full suite
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo --filter "FullyQualifiedName~ShopPaymentIntegrationTests"
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Manual (live API, port 5080, after creating an order via B031's endpoint):

```bash
# initiate
curl -X POST http://<host>:5080/api/shop/<tenantId>/orders/<orderId>/payments/initiate
# → 200 {"gatewayReference":"<32-hex>","redirectUrl":"/shop/<tenantId>/payments/sandbox/<32-hex>"}

# approve
curl -X POST http://<host>:5080/api/shop/<tenantId>/orders/<orderId>/payments/callback \
  -H "Content-Type: application/json" \
  -d '{"gatewayReference":"<ref>","approved":true}'
# → 200 {"orderId":"<orderId>","status":"Paid"}

# repeat the exact same callback
# → 409 "Order already resolved"

# fresh order → initiate → decline
# → 200 {"orderId":"...","status":"PendingPayment"} (order stays payable, attempt Failed)

# unknown reference with approved:false
# → 404
```

Observed in this delivery: all seven demo steps returned the expected statuses; a direct `psql` check showed exactly two attempts — `Succeeded`/`Paid` and `Failed`/`PendingPayment` — each with a populated `callback_received_at_utc`.

## 7. Three review questions

1. `VerifyCallbackAsync` returns `PaymentVerification(false)` for three different situations (unknown reference, already-resolved attempt, and a legitimate decline). How does `PaymentsFeature` use the attempt row to keep those three cases distinct, and which HTTP status would the shopper's fake bank page see in each?
2. The unique index on `gateway_reference` and the resolve-once `TryResolve` guard both prevent double resolution. If one of them were removed, which failure mode would remain possible, and which integration fact would catch it?
3. Why is the gateway registered as **scoped** rather than singleton, and what would break at request time if it were registered as a singleton today (hint: look at what it holds and what holds the database connection)?
