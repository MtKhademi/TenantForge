# B044 — Gateway-neutral, idempotent payment lifecycle

Slice S40. Payment initiation and verification are now provider-neutral and
idempotent: `POST …/payments/initiate` takes an `Idempotency-Key` header and
no body, a new `GET …/payments/status` is token-protected, the old
browser-authored `POST …/payments/callback` is deleted, and the Development-only
`POST …/payments/sandbox/resolve` is the only browser-driven simulation. The
browser never declares success. `Shop:Payments:Provider` selects the gateway
(Sandbox today, ZarinPal in B045) and Production refuses the sandbox at
startup.

## 1. Files changed and why

- `domain/ShopPaymentAttempt.cs` — six new fields: `AmountSnapshot` (the order
  total frozen at initiation), `CallbackTokenHash` (SHA-256 of the 32-byte raw
  token; the raw token is never stored), `FailureCode` (stable code on
  `Failed`), `ProviderReference` (the provider's verification-time reference,
  never the authority), `VerifiedAtUtc`, `Version`. The enum gains
  `Invalidated`. `TryResolve` now takes `(succeeded, providerReference,
  failureCode, nowUtc)` and `TryInvalidate` is new — both idempotent, both
  called only by the completion service.
- `domain/ShopPaymentInitiation.cs` (new) — the idempotency record:
  `IdempotencyKey`, `RequestFingerprint`, `RedirectUrl`, `TokenSeed` (32 bytes),
  `AttemptId`. The unique `(tenant_id, idempotency_key)` index makes a key
  single-use per tenant.
- `domain/ShopOrder.cs` — `MarkPaid`'s doc updated: it is called only by the
  completion service and **deliberately does not bump `Version`** (only operator
  mutations are `expectedVersion`-gated).
- `features/payments/IShopPaymentGateway.cs` — replaced with the provider-neutral
  shapes: `PaymentContext`, `GatewayInitiation`, `PaymentVerificationRequest`,
  `GatewayVerification`, the two-member `GatewayOutcome` enum and
  `IShopPaymentGateway { Provider, InitiateAsync, VerifyAsync }`. `VerifyCallbackAsync`
  and the old records are gone.
- `features/payments/SandboxPaymentGateway.cs` — rewritten as a pure decision
  function: mints a 32-hex authority + the relative `/shop/{tenantId}/bank?authority=…`
  redirect; maps the page's `approved` value to a `GatewayVerification`. No DB,
  no outbound HTTP.
- `features/payments/ShopPaymentGatewayResolver.cs` (new) — `IShopPaymentGatewayResolver`
  picks the one registered gateway whose `Provider` matches the config value;
  Development defaults to Sandbox, everything else fails closed; a configured
  provider with no registered gateway fails closed.
- `features/payments/ShopPaymentCompletionService.cs` (new) — the ONLY place an
  attempt leaves `Initiated` or an order moves to `Paid`: `VerifyAsync` (row
  locks, validation, exactly-once resolution) and `InvalidateInitiatedAttemptsAsync`
  (called by cancel).
- `features/payments/PaymentsFeature.cs` — the three routes; the old callback
  route is deleted.
- `features/payments/PaymentContracts.cs` — `InitiatePaymentResponse`,
  `PaymentStatusResponse`, `ResolveSandboxPaymentRequest` (public wire records).
- `features/orders/AdminOrdersFeature.cs` — the cancel transition now calls
  `completionService.InvalidateInitiatedAttemptsAsync` instead of mutating
  attempts directly.
- `ShopConfig.cs` — `Shop:Payments:Provider` validation + DI registrations for
  the resolver and completion service (both gateways registered; the resolver
  picks, never registration order).
- `infrastructure/ShopPaymentAttemptMap.cs`, `ShopPaymentInitiationMap.cs` (new),
  `ShopDbContext.cs` — column maps, the new table + unique index, the new `DbSet`.
- `infrastructure/Migrations/20260923163122_AddShopPaymentLifecycle.{cs,Designer.cs}`
  (generated) + `ShopDbContextModelSnapshot.cs` — six attempt columns + the new
  table; nothing else.
- Tests: `ShopPaymentLifecycleIntegrationTests.cs` (new, 16 facts on a dedicated
  `ShopPaymentLifecycleDbFixture`/collection), `ShopPaymentIntegrationTests.cs`
  **deleted** (it exercised the removed callback route), and the payment helpers
  in `ShopOrderLookupIntegrationTests.cs` / `ShopOrderOperationsIntegrationTests.cs`
  / `ShopAdminOrdersIntegrationTests.cs` migrated to the new contract.
  `ApiFactory` sets `Shop:Payments:Provider` per environment; `shop_payment_initiations`
  added to the `ExpectedShopTables` roster.

## 2. Request flow, endpoint to response

`POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate` (anonymous).

1. Parse both ids (malformed → the one generic `404`); read the
   `Idempotency-Key` header directly (missing or non-UUID → `400` naming the
   header).
2. `gatewayResolver.Resolve()` picks the gateway; build the server-owned status
   callback URL; compute the canonical `fingerprint = SHA256("tenantId|orderId")`.
3. Begin a transaction and **lock the order row `FOR UPDATE`** (tenant-first).
   Missing/foreign → `404`; not `PendingPayment` → `409`.
4. Same key already stored: same fingerprint → rebuild the stored response
   (byte-identical, token re-derived from the seed) and commit; different
   fingerprint → `409 idempotency_key_conflict`.
5. A live `Initiated` attempt exists: store this key against it and return its
   stored response (one live attempt per order, no orphan rows).
6. Total attempts ≥ 10 → `409 too_many_payment_attempts`.
7. Otherwise mint: a 32-byte seed, its token = `SHA256(seed)`, store only
   `SHA256(token)`; call `gateway.InitiateAsync`; persist the `ShopPaymentAttempt`
   and the `ShopPaymentInitiation`; return `{ provider, redirectUrl, resultToken }`.
8. A `DbUpdateException` that is a PostgreSQL `23505` on
   `ix_shop_payment_initiations_tenant_idempotency_key` is the last-resort same-key
   race: roll back, re-read the winner, answer replay or conflict — never a `500`.

`GET …/payments/status?token=…`: scope the order by tenant, hash the presented
token, compare to the stored hashes with a **fixed-time** comparison, and return
only `{ orderNumber, status, providerReference }` — every miss is the one
identical `404`.

`POST …/payments/sandbox/resolve` (Development only): the fake bank page's
`{ authority, approved }` → `gateway.VerifyAsync` →
`completionService.VerifyAsync`, which locks the order then the attempt,
validates tenant/order/amount/provider/status, and resolves the attempt exactly
once — `Succeeded`+`MarkPaid` on a success for a `PendingPayment` order, `Failed`
with a stable code otherwise, or the already-computed outcome when the attempt
was already resolved (e.g. invalidating by a cancel).

## 3. Backend concepts introduced

- **A gateway seam**: the provider is a pure function (`InitiateAsync`/
  `VerifyAsync` on a `PaymentContext`) that never sees the order, the DB or the
  raw token; the feature mints the rows and the completion service applies
  transitions, so swapping in ZarinPal touches neither.
- **Configuration-selected strategy, not registration order**: both gateways are
  registered; the resolver matches `Provider` to the config value explicitly and
  fails closed on a value with no implementation.
- **The single transition owner**: one class, under row locks, is the only
  place an attempt leaves `Initiated` or an order becomes `Paid` — a gateway
  result is a hint, never a command.
- **Idempotency by a unique constraint + a stored seed**: a same-key retry
  replays byte-identically because the response is re-derived from the stored
  row (seed → token), and the unique index, not an `AnyAsync`, resolves a racing
  first-call to one winner.
- **Hashed, fixed-time-verified credentials**: the raw callback token is stored
  only as its SHA-256, and the status route compares hashes in fixed time so the
  correct token cannot be learned byte-by-byte.
- **Fail-closed development simulation**: the sandbox route is mapped only in
  Development, and Production with `Shop:Payments:Provider=Sandbox` refuses to
  start — the simulation cannot be enabled silently in production.

## 4. Security decisions

- **The browser never declares success**: initiation has no body; verification
  runs `gateway.VerifyAsync` → the completion service, which re-checks
  tenant/order/amount/provider under row locks before anything moves.
- **Token is the only status credential**: returned once, stored only as a
  hash, compared in fixed time; every status miss is one identical `404` so a
  caller cannot distinguish wrong token from wrong order from wrong tenant.
- **Tenant isolation**: the attempt has no tenant column, so both routes reach
  it through the order row locked with a tenant-first predicate.
- **Default deny + fail closed**: missing provider outside Development throws at
  startup; Sandbox outside Development throws; a configured-but-unregistered
  provider throws at first resolution.
- **Late payment safety**: cancel invalidates live attempts in its transaction,
  so a late success finds a non-`Initiated` attempt, returns the order's current
  status, and the order never becomes `Paid`.
- **No secrets logged**: the raw token, authority and gateway references are
  never logged; only stable ids and reason codes are.

## 5. Alternatives deliberately postponed

- A real ZarinPal gateway (B045) — the seam is ready but no outbound
  provider call exists yet; `ZarinPal` config fails closed until then.
- A shared, reusable idempotency table/middleware for all endpoints — the
  mechanism stays local to the payments feature.
- A `Shop.Contract` project — still no second .NET consumer of a Shop shape.
- Refunds, reversals, saved payment methods, cards and multi-currency — each is
  its own later slice.
- Storing the raw token for convenience — rejected; only its hash is persisted
  and the token is re-derived from the seed on replay.

## 6. Commands and manual steps

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj \
  --nologo --filter FullyQualifiedName~ShopPaymentLifecycleIntegrationTests
dotnet.exe test TenantForge.sln --nologo
git diff --name-only origin/main... -- src/web   # prints nothing
```

Manual demo (API running in Development, `Shop:Payments:Provider` unset →
Sandbox):

1. Create a tenant + single-variant product + an anonymous order
   (`PendingPayment`).
2. `POST …/orders/{id}/payments/initiate` with `Idempotency-Key: <uuid>` →
   `200 { provider:"Sandbox", redirectUrl:"/shop/{tenant}/bank?authority=…",
   resultToken:"<64-hex>" }`.
3. Repeat the exact request → `200` byte-identical; a fresh key → the same
   `redirectUrl`/`resultToken` (one live attempt).
4. Open the in-app bank page and approve →
   `POST …/payments/sandbox/resolve { authority, approved:true }` → `200 {
   status:"Paid" }`.
5. `GET …/payments/status?token=<resultToken>` → `200 { status:"Paid" }`; a
   wrong token → the same generic `404` as a wrong order.
6. For a second order, cancel it, then send a late `approved:true` → the
   response reports `Cancelled` and the order never becomes `Paid`.
7. A 10-attempt order: the 11th initiate → `409 too_many_payment_attempts`.

## 7. Three review questions

1. Initiation stores a random `TokenSeed` on the idempotency row and returns
   `resultToken = SHA256(seed)` to the client, but persists only
   `SHA256(resultToken)` on the attempt. Why store the *seed* (rather than the
   token) for replay, and why is storing the seed not a credential leak?
2. A cancel invalidates an order's `Initiated` attempts in the same
   transaction. Given that, why does a late success return the order's current
   `Cancelled` status (`200`) instead of the `409 order_not_payable` branch —
   and when *would* that 409 branch ever be reached?
3. The completion service locks the order row and then the attempt row,
   validates amount/provider/status, and resolves the attempt exactly once. If
   it instead did a bare `AnyAsync` "is this attempt still Initiated?" before
   writing, which concrete double-payment race would slip through, and which
   stored field would catch it if it somehow did?
