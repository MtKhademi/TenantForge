---
id: B032
slice: S29
title: Sandbox payment API
agent: backend-mentor
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Add the `IShopPaymentGateway` abstraction and its one real implementation,
`SandboxPaymentGateway`, plus an endpoint to initiate payment for a
`PendingPayment` order (returning a redirect target the frontend renders
as a fake bank page) and a callback endpoint that verifies the attempt and
transitions the order to `Paid` or back to `PendingPayment`/`Failed`.

# Context

Read `tasks/slices/029-shop-order-and-sandbox-payment.md` completely, in
particular its "Payment abstraction" section and the explicit,
named non-goal: no real ZarinPal integration, no stored card data, no
webhook signature scheme beyond what `SandboxPaymentGateway` needs to
demonstrate the seam. Read the already-delivered
`features/orders/OrderCreationFeature.cs` (B031) for the `ShopOrder`
shape this task reads and mutates.

# Scope

1. `domain/ShopPaymentAttempt.cs` and its map, added to `ShopDbContext`.
   One EF Core migration adding this one table.
2. `features/payments/IShopPaymentGateway.cs`:
   ```csharp
   internal interface IShopPaymentGateway
   {
       Task<PaymentInitiation> InitiateAsync(ShopOrder order, CancellationToken ct);
       Task<PaymentVerification> VerifyCallbackAsync(string gatewayReference, bool approved, CancellationToken ct);
   }
   ```
   (`PaymentInitiation`/`PaymentVerification` are small plain records
   defined alongside the interface — not contract types, since nothing
   outside this module ever sees them.)
3. `features/payments/SandboxPaymentGateway.cs`: `InitiateAsync` generates
   a `GatewayReference` (a random string), persists a `ShopPaymentAttempt`
   row (`Status = Initiated`) and returns a redirect target that is
   simply an in-app frontend route carrying the order id and gateway
   reference (for example `/shop/{tenantId}/payments/sandbox/{gatewayReference}`
   — the exact path is a frontend routing concern F038/F039 own; this task
   only needs to return a string the frontend can navigate to). No
   outbound HTTP call to any external service exists anywhere in this
   class. `VerifyCallbackAsync` looks up the `ShopPaymentAttempt` by
   `GatewayReference`, sets its `Status` to `Succeeded`/`Failed` and
   `CallbackReceivedAtUtc`, and returns whether the order should become
   `Paid`.
4. `features/payments/PaymentsFeature.cs`:
   - `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate` —
     anonymous. Requires the order to be `PendingPayment` for this tenant
     (any other status is a clear conflict error, e.g. already paid).
     Calls `IShopPaymentGateway.InitiateAsync` and returns the redirect
     target.
   - `POST /api/shop/{tenantId}/orders/{orderId}/payments/callback` —
     anonymous. Body: `gatewayReference`, `approved` (bool — this is the
     shopper's approve/decline choice on the fake bank page, standing in
     for whatever signed payload a real gateway would send). Calls
     `VerifyCallbackAsync`; on success sets the order `Status = Paid`; on
     failure leaves/sets the order `Status = PendingPayment` and records
     the attempt as `Failed`. A callback for an already-`Paid` order or an
     unknown `gatewayReference` is rejected with a clear error rather than
     silently re-applying.
5. Register `IShopPaymentGateway` → `SandboxPaymentGateway` in
   `ShopConfig.RegisterServices` (scoped, since it depends on the scoped
   `ShopDbContext` — matching the same scoped-vs-singleton reasoning
   `IAMConfig.RegisterServices` already documents for its own
   database-backed services).

# Non-goals

- No real ZarinPal (or other) integration, credentials, or production
  webhook-signature verification — restated from the slice, binding for
  this task specifically since it is the one that could otherwise be
  tempted to "just add the real thing while we're here."
- No `Cancelled`/`Fulfilled` order-status transition from this endpoint.
- No retry/idempotency-key handling beyond rejecting a callback for an
  order that is not `PendingPayment` — a more sophisticated idempotency
  scheme is deferred until a real gateway (with its own retry semantics)
  is actually integrated.

# Acceptance

- Initiating payment for a `PendingPayment` order creates a
  `ShopPaymentAttempt` and returns a redirect target.
- Initiating payment for a non-`PendingPayment` order (e.g. already
  `Paid`) is rejected with a clear conflict error.
- An `approved: true` callback with a valid `gatewayReference` transitions
  the order to `Paid` and the attempt to `Succeeded`.
- An `approved: false` callback leaves the order `PendingPayment` and
  marks the attempt `Failed`.
- A callback with an unknown `gatewayReference`, or a second callback for
  an already-resolved attempt, is rejected with a clear error, not
  silently accepted.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: initiate on a valid order, initiate on an
invalid-status order, approve callback reaching `Paid`, decline callback
staying `PendingPayment`, and rejection of an unknown/duplicate callback.
The full existing IAM suite continues to pass unmodified.

Manual:

- Create an order (B031), initiate payment, call the callback endpoint
  with `approved: true` and confirm the order is `Paid`; repeat with a
  fresh order and `approved: false` and confirm it stays `PendingPayment`.

# Lifecycle

Add row `B032` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B031`, and Spec link
`tasks/backend/B032-sandbox-payment-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
