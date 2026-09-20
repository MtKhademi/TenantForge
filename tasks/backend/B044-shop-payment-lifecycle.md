# B044 — Make payment initiation and verification gateway-neutral and idempotent

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S40`.
- Depends on: `B032, B043`.
- Immediate browser consumer: `F062`. Do not widen this API for an unnamed future screen.
- Visible outcome: The existing sandbox remains demoable while payment attempts gain amount snapshots, idempotent initiation and a server-owned verification result suitable for a real provider.

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

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Extend attempt with `AmountSnapshot`, `CallbackTokenHash`, `FailureCode?`, `ProviderReference?`, `VerifiedAtUtc?`, `Version`. Status values: Initiated, Succeeded, Failed, Invalidated. Store only SHA-256 of a 32-byte random callback token; return the raw token once to the client result URL.
        2. Replace `VerifyCallbackAsync(string gatewayReference, bool approved)` with provider-neutral `InitiateAsync(PaymentContext, ct)` and `VerifyAsync(PaymentVerificationRequest, ct)`. Verification result contains `Outcome`, provider reference and stable error code. Provider result can never directly mutate an order.
        3. Initiation requires `Idempotency-Key`, checks PendingPayment, and replays the same response for the same canonical request. At most one non-invalidated Initiated attempt per order; retry returns it instead of minting orphan rows. Cap attempts per order at 10.
        4. A single `ShopPaymentCompletionService` locks attempt and order, validates tenant/order/amount/provider/status, resolves the attempt once and changes PendingPayment -> Paid only for verified success. Duplicate provider callbacks return the already-computed outcome. Cancelled/Fulfilled orders never become Paid.
        5. Add `Shop:Payments:Provider` with exact values `Sandbox` or `ZarinPal`. Register both implementations but resolve the selected gateway through a small `IShopPaymentGatewayResolver`; never overwrite one registration by order. Sandbox approve/decline endpoint is mapped only in Development, and Production activation fails if provider `Sandbox` is selected. Remove the current `Approved` boolean from the general callback request.
        6. Status lookup requires the raw callback token, uses fixed-time hash comparison, and returns only order number/status/provider reference. Invalid combinations use one 404 body.


## Required code shape

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

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

initiation replay; one live attempt; amount snapshot; attempt cap; success/failure/duplicate completion; cancelled order race; token hash/no raw persistence; unknown token generic 404; sandbox Development works and Production selection fails closed; old migrations upgrade.

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
