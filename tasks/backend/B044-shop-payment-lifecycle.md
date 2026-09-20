# B044 — Make payment initiation and verification gateway-neutral and idempotent

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S40`.
- Depends on: `B032, B043`.
- Immediate browser consumer: `F062`. Do not widen this API for an unnamed future screen.
- Visible outcome: The existing sandbox remains demoable while payment attempts gain amount snapshots, idempotent initiation and a server-owned verification result suitable for a real provider.

## Do this in order

Before anything else, do the boilerplate from `AGENTS.md` and the Ownership
section above: read `AGENTS.md`, read the `B044` row in `tasks/TASKS.md`,
read this whole Spec, read `docs/modules/SHOP.md` if it exists, read
`tasks/slices/040-shop-payment-lifecycle.md`, read the matching section of
`docs/design/shop/http-contracts.md`, follow the branch-naming rule
(`backend/b044-<slug>`) and the ledger-update rule (only the `B044` row
changes status) already described above. Then follow these steps. They cover
the same ground as the "Required implementation" and "Required code shape"
sections below in a stricter order — read those sections too, they contain
detail not repeated here.

1. Confirm baseline commit `34dc44e` still matches current code: one Shop
   module project, internal EF entities, TSID IDs ("TSID" = a sortable
   numeric string ID type — see `TenantForge.BuildingBlocks`), a separate
   migration-history table, raw-SQL IAM membership/role checks, anonymous
   storefront/cart/checkout/order/payment/lookup routes, and integration
   tests under `Shop*IntegrationTests.cs`. If something differs, note it in
   your plan before continuing.
2. In the payment domain entity for a payment attempt, add these fields
   exactly: `AmountSnapshot` (decimal), `CallbackTokenHash` (string),
   `FailureCode` (nullable string), `ProviderReference` (nullable string),
   `VerifiedAtUtc` (nullable `DateTimeOffset`), `Version` (used for optimistic
   concurrency / row locking). Add a `Status` enum with exactly these values:
   `Initiated`, `Succeeded`, `Failed`, `Invalidated`.
3. Never store the raw callback token. Generate a 32-byte random token,
   compute its SHA-256 hash, store only the hash in `CallbackTokenHash`,
   and return the raw token exactly once, embedded in the client result URL
   returned from initiation.
4. Delete the old `VerifyCallbackAsync(string gatewayReference, bool approved)`
   method entirely. Replace it with two methods on the gateway interface:
   `InitiateAsync(PaymentContext, CancellationToken)` and
   `VerifyAsync(PaymentVerificationRequest, CancellationToken)`. The exact
   shapes are in "Required code shape" below — implement every member, do not
   leave placeholders. The verification result must carry an `Outcome`, a
   provider reference, and a stable error code. Never let a raw provider
   result directly mutate an order — always go through the completion service
   from step 6.
5. Implement idempotent initiation:
   - Require the `Idempotency-Key` request header (a UUID) on
     `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate`.
   - Before creating a new attempt, check whether the order's status is
     `PendingPayment`. If not, reject.
   - If a non-invalidated `Initiated` attempt already exists for this order,
     return that same attempt's response instead of creating a new one. An
     order may have at most one non-invalidated `Initiated` attempt at a
     time.
   - Cap the number of attempts per order at exactly 10. Reject the 11th.
   - Store enough of the request to detect a retry with the same
     `Idempotency-Key` and canonical request body, and replay the same
     response for it instead of creating a duplicate.
6. Create one new class, `ShopPaymentCompletionService`, that is the only
   place allowed to move an attempt from `Initiated` to `Succeeded` or
   `Failed`, and the only place allowed to move an order from `PendingPayment`
   to `Paid`. It must:
   - Take a row lock (or equivalent) on both the attempt and the order before
     changing either.
   - Validate, before changing anything: the tenant matches, the order
     matches, the amount matches `AmountSnapshot`, the provider matches, and
     the attempt's current status allows this transition.
   - Resolve each attempt exactly once. A second call for an attempt that is
     already resolved must return the already-computed outcome instead of
     re-running the transition (this makes duplicate provider callbacks
     idempotent).
   - Only ever set the order to `Paid` when verification outcome is success.
   - Never allow an order that is already `Cancelled` or `Fulfilled` to become
     `Paid`, even if a late/duplicate success callback arrives for it.
7. Add a configuration value `Shop:Payments:Provider` that accepts exactly
   the strings `Sandbox` or `ZarinPal` and nothing else.
   - Register both `IShopPaymentGateway` implementations (Sandbox and
     ZarinPal, the latter added in a later task) in the DI container.
   - Add a new small class `IShopPaymentGatewayResolver` whose job is to pick
     the implementation matching `Shop:Payments:Provider`. Never resolve the
     gateway by registration order — always resolve it explicitly by the
     `Provider` string.
   - Map the sandbox approve/decline endpoint
     (`POST /api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve`)
     only when the ASP.NET Core environment is Development. In Production,
     if `Shop:Payments:Provider` is set to `Sandbox`, application startup
     must fail closed (throw / refuse to start) rather than silently allow
     it.
   - Delete the old general callback route that accepted a browser-authored
     `Approved` boolean (a client-supplied "trust me, it succeeded" flag) —
     remove that boolean from any remaining request type. The sandbox resolve
     route above is now the only browser-driven simulation, and it cannot be
     mapped outside Development.
8. Implement the status lookup endpoint,
   `GET /api/shop/{tenantId}/orders/{orderId}/payments/status?token={resultToken}`:
   - Require the raw callback token from step 3 as the `token` query value.
   - Compare it to the stored hash using a fixed-time comparison (a
     comparison whose running time does not depend on where the strings first
     differ — this avoids leaking the correct token one byte at a time
     through timing). Do not use a normal `==` or `string.Equals` for this
     comparison.
   - On success, return only order number, status, and provider reference —
     nothing else.
   - On any invalid combination (wrong token, wrong order, wrong tenant),
     return the exact same generic 404 body every time, so an attacker cannot
     distinguish "wrong token" from "order doesn't exist".
9. Write an EF migration (EF migration = a generated script that changes the
   database schema; create it by running
   `dotnet ef migrations add <DescriptiveName>` inside the Shop module
   project) for the new attempt fields and enum. Inspect the generated file
   and confirm it only contains the intended schema changes.
10. Write every integration test listed in "Integration tests required"
    below — see that section for the literal, one-test-per-scenario list.
11. Update `docs/modules/SHOP.md` (create it if it does not exist) so its
    routes/entities/config/auth/tests sections match what you built, and
    update the matching section of `docs/design/shop/http-contracts.md` to
    the delivered wire contract.
12. Write the `docs/learning/B044-<slug>.md` learning note per `AGENTS.md`'s
    "Backend learning note" section.
13. Run every command in "Validation" below, in order, and fix failures
    before presenting the final diff for approval.
14. Work through the "Acceptance checklist" at the end of this file item by
    item before asking for final approval.

## Read before editing

Read `AGENTS.md`, the `B044` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/040-shop-payment-lifecycle.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

payment domain/map/contracts/interface/sandbox/feature, order feature, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Request | Success |
|---|---|---|---|
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/initiate` | `Idempotency-Key` UUID header | `200 InitiatePaymentResponse` |
| GET | `/api/shop/{tenantId}/orders/{orderId}/payments/status?token={resultToken}` | none | `200 PaymentStatusResponse` |
| POST | `/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve` | Development only; `ResolveSandboxPaymentRequest` | `200 PaymentStatusResponse` |

Remove the old general callback that accepts a browser-authored `Approved`
boolean. The sandbox resolve route is the only browser-driven simulation and
cannot be mapped outside Development.

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = the repo's standard JSON error body shape for HTTP errors; reuse the existing helper, do not invent a new error format). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Extend attempt with `AmountSnapshot`, `CallbackTokenHash`, `FailureCode?`, `ProviderReference?`, `VerifiedAtUtc?`, `Version`. Status values: Initiated, Succeeded, Failed, Invalidated. Store only SHA-256 of a 32-byte random callback token; return the raw token once to the client result URL.
2. Replace `VerifyCallbackAsync(string gatewayReference, bool approved)` with provider-neutral `InitiateAsync(PaymentContext, ct)` and `VerifyAsync(PaymentVerificationRequest, ct)`. Verification result contains `Outcome`, provider reference and stable error code. Provider result can never directly mutate an order.
3. Initiation requires `Idempotency-Key`, checks PendingPayment, and replays the same response for the same canonical request. At most one non-invalidated Initiated attempt per order; retry returns it instead of minting orphan rows. Cap attempts per order at 10.
4. A single `ShopPaymentCompletionService` locks attempt and order, validates tenant/order/amount/provider/status, resolves the attempt once and changes PendingPayment -> Paid only for verified success. Duplicate provider callbacks return the already-computed outcome. Cancelled/Fulfilled orders never become Paid.
5. Add `Shop:Payments:Provider` with exact values `Sandbox` or `ZarinPal`. Register both implementations but resolve the selected gateway through a small `IShopPaymentGatewayResolver`; never overwrite one registration by order. Sandbox approve/decline endpoint is mapped only in Development, and Production activation fails if provider `Sandbox` is selected. Remove the current `Approved` boolean from the general callback request.
6. Status lookup requires the raw callback token, uses fixed-time hash comparison, and returns only order number/status/provider reference. Invalid combinations use one 404 body.

## Required code shape

Implement every member below completely — do not leave any of it as a
placeholder or a "TODO" comment. The names, types and visibility (`internal`
vs `public`) must match exactly.

```csharp
internal sealed record PaymentContext(Tsid TenantId, Tsid OrderId, decimal Amount, Uri CallbackUri);
internal sealed record GatewayInitiation(string Authority, Uri RedirectUri);
internal sealed record PaymentVerificationRequest(string Authority, IReadOnlyDictionary<string,string> CallbackValues);
internal sealed record GatewayVerification(GatewayOutcome Outcome, string? ReferenceId, string? ErrorCode);
internal interface IShopPaymentGateway
{
    string Provider { get; }
    Task<GatewayInitiation> InitiateAsync(PaymentContext context, CancellationToken ct);
    Task<GatewayVerification> VerifyAsync(PaymentVerificationRequest request, CancellationToken ct);
}

public sealed record InitiatePaymentResponse(
    string Provider, string RedirectUrl, string ResultToken);
public sealed record PaymentStatusResponse(
    string OrderNumber, string Status, string? ProviderReference);
public sealed record ResolveSandboxPaymentRequest(string Authority, bool Approved);
```

`GatewayOutcome` is an enum you add (e.g. `Succeeded`, `Failed`) that
`GatewayVerification.Outcome` uses — pick the exact success/failure member
names your codebase's existing enum-naming convention uses; if unsure, use
`Succeeded` and `Failed`.

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each test should do the described action,
then assert the described outcome — do not combine several scenarios into
one test.

- [ ] Write a test that retries initiation with the same `Idempotency-Key` and body, then asserts the same response is replayed and no duplicate attempt row is created.
- [ ] Write a test that initiates twice for the same order without a matching idempotency replay, then asserts only one non-invalidated `Initiated` attempt exists.
- [ ] Write a test that completes payment, then asserts the persisted attempt's `AmountSnapshot` matches the amount at initiation time even if the order's price later changes.
- [ ] Write a test that attempts to initiate an 11th payment attempt for one order, then asserts it is rejected and no 11th row is created.
- [ ] Write a test that verifies a successful payment, then asserts the order moves to `Paid` and the attempt to `Succeeded`.
- [ ] Write a test that verifies a failed payment, then asserts the order stays `PendingPayment` and the attempt becomes `Failed`.
- [ ] Write a test that sends the same successful provider callback twice, then asserts the second call returns the already-computed outcome and does not re-apply side effects.
- [ ] Write a test that sends a late success callback for an order that is already `Cancelled`, then asserts the order does not become `Paid`.
- [ ] Write a test that reads the stored attempt row after a successful flow, then asserts only a SHA-256 hash of the callback token is stored, never the raw token.
- [ ] Write a test that calls the status endpoint with an unknown or wrong token, then asserts a generic 404 body identical to the "wrong order" case.
- [ ] Write a test in the Development environment that calls the sandbox resolve endpoint, then asserts it succeeds.
- [ ] Write a test that configures `Shop:Payments:Provider=Sandbox` in the Production environment, then asserts startup/activation fails closed.
- [ ] Write a test that applies the new EF migration on top of the existing migration history, then asserts it upgrades cleanly with no conflicts.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F062` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

A real provider, refund/reversal, cards, saved payment methods or multi-currency.

## Acceptance checklist

- [ ] The visible outcome works through `F062` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
