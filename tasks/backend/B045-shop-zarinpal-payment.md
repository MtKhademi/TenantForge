# B045 — Integrate ZarinPal request and server-side verification

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S41`.
- Depends on: `B044`.
- Immediate browser consumer: `F062`. Do not widen this API for an unnamed future screen.
- Visible outcome: Production checkout redirects to ZarinPal and trusts an order payment only after the backend verifies authority and amount with the provider.

## Read before editing

Read `AGENTS.md`, the `B045` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/041-shop-zarinpal-payment.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

ZarinPal options/client/gateway/contracts, payment routes/config, integration tests with fake HttpMessageHandler, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

Initiate returns provider redirect. Add anonymous `GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`; backend verifies then 302 redirects to the configured frontend payment-result URL with opaque result token only.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add `ZarinPalOptions`: `MerchantId`, `Currency` (`IRR` or `IRT`), `RequestEndpoint`, `VerifyEndpoint`, `GatewayBaseUrl`, `PublicApiBaseUrl`, `FrontendResultBaseUrl`, timeout. Secrets come from configuration/environment only and are never returned/logged/stored per order. Validate HTTPS and exact allowlisted hosts in Production.
        2. Add typed `HttpClient` and `ZarinPalPaymentGateway`. Request sends merchant ID, checked integer amount in configured currency, description and backend callback URL. Parse the official v4 envelope. Accept request code 100 only and require a non-empty authority.
        3. Generate a signed, expiring state value binding tenant ID, order ID, attempt ID and callback token with ASP.NET Core Data Protection (`IDataProtector`, purpose `TenantForge.Shop.ZarinPal.Callback.v1`, 30-minute lifetime). Do not take order/amount from query parameters. On callback, `Status != OK` resolves as declined without calling verify; `OK` calls verify server-to-server using stored authority and amount.
        4. Verification code 100 is success. Code 101 is treated as already verified and must reconcile only when the returned reference matches a previously stored success for this attempt; otherwise fail closed and log structured identifiers without secrets. Every other code is failure.
        5. Convert the order decimal amount to provider integer with checked arithmetic and exactly one currency conversion. Current UI labels Toman, so default `IRT`; tests cover both configured units.
        6. Use cancellation tokens, 10-second default timeout and safe provider-unavailable mapping. A network timeout keeps attempt Initiated so retry/reconciliation is possible; it never marks Paid.
        7. Keep Sandbox selectable in Development. Do not auto-fallback from ZarinPal to Sandbox. Cite the exact official request/verify documentation and review date in the learning note.


## Required code shape

```csharp
        internal sealed class ZarinPalOptions
        {
            internal const string SectionPath = "Shop:Payments:ZarinPal";
            public string MerchantId { get; init; } = string.Empty;
            public string Currency { get; init; } = "IRT";
            public Uri RequestEndpoint { get; init; } = null!;
            public Uri VerifyEndpoint { get; init; } = null!;
            public Uri GatewayBaseUrl { get; init; } = null!;
            public Uri PublicApiBaseUrl { get; init; } = null!;
            public Uri FrontendResultBaseUrl { get; init; } = null!;
        }

        internal sealed record ZarinPalRequestData(int Code, string Authority, string? Message);
        internal sealed record ZarinPalVerifyData(int Code, long? RefId, string? CardPan, string? Message);
        ```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

request JSON and redirect URL; IRR/IRT conversion; code 100/101/decline/error; mismatched authority/amount; signed state tamper/expiry; duplicate callback; timeout remains pending; cancelled order late success; Production host/config validation; no secrets in response/log fixtures.

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

Refund, inquiry scheduler, multiple live merchant accounts, webhook, split payment or fee calculation.

## Acceptance checklist

- [ ] The visible outcome works through `F062` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
