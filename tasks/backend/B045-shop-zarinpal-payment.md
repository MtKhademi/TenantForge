# B045 — Integrate ZarinPal request and server-side verification

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S41`.
- Depends on: `B044`.
- Immediate browser consumer: `F062`. Do not widen this API for an unnamed future screen.
- Visible outcome: Production checkout redirects to ZarinPal and trusts an order payment only after the backend verifies authority and amount with the provider.

## Do this in order

Before anything else, do the boilerplate from `AGENTS.md` and the Ownership
section above: read `AGENTS.md`, read the `B045` row in `tasks/TASKS.md`,
read this whole Spec, read `docs/modules/SHOP.md` if it exists, read
`tasks/slices/041-shop-zarinpal-payment.md`, read the matching section of
`docs/design/shop/http-contracts.md`, follow the branch-naming rule
(`backend/b045-<slug>`) and the ledger-update rule (only the `B045` row
changes status) already described above. Then follow these steps. They cover
the same ground as the "Required implementation" and "Required code shape"
sections below in a stricter order — read those sections too, they contain
detail not repeated here.

1. Confirm baseline commit `34dc44e` still matches current code (same
   baseline description as B044: one Shop module project, internal EF
   entities, TSID IDs, separate migration-history table, raw-SQL IAM checks,
   anonymous storefront/cart/checkout/order/payment/lookup routes, exact
   tests under `Shop*IntegrationTests.cs`). Confirm `B044`'s
   `IShopPaymentGateway` interface and `ShopPaymentCompletionService` exist —
   this task plugs a real implementation into that interface, it does not
   change it.
2. Add a new options class `ZarinPalOptions` bound to configuration section
   path `Shop:Payments:ZarinPal`. Give it exactly these members:
   `MerchantId` (string), `Currency` (string, only `IRR` or `IRT` allowed),
   `RequestEndpoint` (Uri), `VerifyEndpoint` (Uri), `GatewayBaseUrl` (Uri),
   `PublicApiBaseUrl` (Uri), `FrontendResultBaseUrl` (Uri), and a timeout
   value. The exact code shape is in "Required code shape" below.
   - Read `MerchantId` and any other secret only from configuration/
     environment variables. Never return it in an HTTP response, never log
     it, never persist it per-order.
   - In the Production environment, validate at startup that every
     configured URL uses HTTPS and is on an explicit allowlist of hosts you
     define for ZarinPal. Fail startup if not.
3. Add a typed `HttpClient` (an `HttpClient` registered through
   `AddHttpClient<T>` so it gets pooling/lifetime management from DI, instead
   of `new HttpClient()`) and a `ZarinPalPaymentGateway` class implementing
   `IShopPaymentGateway` from B044.
   - `InitiateAsync` must call `RequestEndpoint` sending: the merchant ID,
     the amount as a checked (overflow-checked) integer in the configured
     currency, a description, and the backend's own callback URL (not a
     frontend URL).
   - Parse the official ZarinPal v4 response envelope (the JSON reply
     format ZarinPal's v4 payment API always uses).
   - Treat request code `100` as the only success code. Also require the
     returned `authority` value to be non-empty; if it is empty, treat the
     call as failed even if the code was 100.
4. Generate a signed, expiring "state" value (an opaque token that proves
   the callback query string was not forged, because it can only have been
   created by the server) using ASP.NET Core Data Protection's
   `IDataProtector`. Use exactly:
   - purpose string `TenantForge.Shop.ZarinPal.Callback.v1`,
   - a 30-minute lifetime,
   - and bind into it: tenant ID, order ID, attempt ID, and the callback
     token from B044.
   Never take order ID or amount from the callback's query parameters
   directly — always read them back out of this signed state.
   On the callback endpoint:
   - If `Status != OK`, resolve the payment as declined and do **not** call
     the ZarinPal verify endpoint at all.
   - If `Status == OK`, call verify server-to-server, using the authority and
     amount you stored server-side (not values from the query string).
5. Implement verification code handling exactly:
   - Code `100` = success.
   - Code `101` = "already verified". Only reconcile this as success if the
     reference ID returned now matches a reference ID you already stored as
     a success for this exact attempt. If it does not match, fail closed
     (treat as failure) and log structured identifiers only — no secrets.
   - Every other code = failure.
6. Convert the order's decimal amount into the provider's required integer
   amount using checked arithmetic (arithmetic that throws on overflow
   instead of silently wrapping) and exactly one currency conversion step.
   The current UI shows amounts in Toman, so the default `Currency` value is
   `IRT`. Write tests that cover both `IRR` and `IRT` configurations (see
   "Integration tests required").
7. Pass a `CancellationToken` through every new async/HTTP call. Use a
   10-second default timeout for calls to ZarinPal. Map a provider-unavailable
   response (timeout, 5xx, malformed body) to a safe failure response. A
   network timeout must leave the attempt in `Initiated` status (never mark
   it `Paid`) so a retry or later reconciliation is still possible.
8. Keep `Sandbox` selectable as `Shop:Payments:Provider` in Development
   (from B044). Do not add any automatic fallback from `ZarinPal` to
   `Sandbox` — if ZarinPal is selected and fails, it fails; the system must
   never silently switch providers.
9. In the learning note, cite the exact official ZarinPal request/verify
   documentation URL(s) you used and the date you reviewed them.
10. **This task adds no EF migration.** Every field it needs already exists
    after `B044`:

    - the ZarinPal `authority` goes in the existing `GatewayReference` column
      (`varchar(60)`, unique — a ZarinPal authority is 36 characters);
    - the `RefId` returned by verify goes in `ProviderReference`;
    - the failure code goes in `FailureCode`, the verification time in
      `VerifiedAtUtc`, the charged amount in `AmountSnapshot`.

    The signed state value from step 4 is a Data Protection token carried in
    the callback query string — it is deliberately **not** persisted, so it
    needs no column. Confirm that no new file appeared under
    `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
    that `ShopDbContextModelSnapshot.cs` is unchanged. If you believe you need
    a new column, stop and report why before adding one — it means this Spec
    or `B044` got something wrong, and adding it silently would break
    `B044`'s entity contract.
11. Write every integration test listed in "Integration tests required"
    below.
12. Update `docs/modules/SHOP.md` and the matching section of
    `docs/design/shop/http-contracts.md` to the delivered wire contract
    (the new callback route).
13. Write the `docs/learning/B045-<slug>.md` learning note per `AGENTS.md`.
14. Run every command in "Validation" below, in order, and fix failures
    before presenting the final diff for approval.
15. Work through the "Acceptance checklist" at the end of this file item by
    item before asking for final approval.

## Read before editing

Read `AGENTS.md`, the `B045` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/041-shop-zarinpal-payment.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Baseline as of commit `34dc44e` (later commits changed only `src/web/**` and
`tasks/**`, so this still describes the backend you will find): Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

ZarinPal options/client/gateway/contracts, payment routes/config, integration tests with fake HttpMessageHandler, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

Initiate returns provider redirect. Add anonymous `GET /api/shop/{tenantId}/payments/zarinpal/callback?Authority=&Status=&state=`; backend verifies then 302 redirects to the configured frontend payment-result URL with opaque result token only.

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = the repo's standard JSON error body shape for HTTP errors; reuse the existing helper, do not invent a new error format). Malformed TSIDs (TSID = a sortable numeric string ID type — see `TenantForge.BuildingBlocks`) and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add `ZarinPalOptions`: `MerchantId`, `Currency` (`IRR` or `IRT`), `RequestEndpoint`, `VerifyEndpoint`, `GatewayBaseUrl`, `PublicApiBaseUrl`, `FrontendResultBaseUrl`, timeout. Secrets come from configuration/environment only and are never returned/logged/stored per order. Validate HTTPS and exact allowlisted hosts in Production.
2. Add typed `HttpClient` and `ZarinPalPaymentGateway`. Request sends merchant ID, checked integer amount in configured currency, description and backend callback URL. Parse the official v4 envelope. Accept request code 100 only and require a non-empty authority.
3. Generate a signed, expiring state value binding tenant ID, order ID, attempt ID and callback token with ASP.NET Core Data Protection (`IDataProtector`, purpose `TenantForge.Shop.ZarinPal.Callback.v1`, 30-minute lifetime). Do not take order/amount from query parameters. On callback, `Status != OK` resolves as declined without calling verify; `OK` calls verify server-to-server using stored authority and amount.
4. Verification code 100 is success. Code 101 is treated as already verified and must reconcile only when the returned reference matches a previously stored success for this attempt; otherwise fail closed and log structured identifiers without secrets. Every other code is failure.
5. Convert the order decimal amount to provider integer with checked arithmetic and exactly one currency conversion. Current UI labels Toman, so default `IRT`; tests cover both configured units.
6. Use cancellation tokens, 10-second default timeout and safe provider-unavailable mapping. A network timeout keeps attempt Initiated so retry/reconciliation is possible; it never marks Paid.
7. Keep Sandbox selectable in Development. Do not auto-fallback from ZarinPal to Sandbox. Cite the exact official request/verify documentation and review date in the learning note.

## Required code shape

Implement every member below completely — do not leave any of it as a
placeholder or a "TODO" comment. The names, types and visibility (`internal`
vs `public`) must match exactly.

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

Write one test per scenario below. Each test should do the described action,
then assert the described outcome — do not combine several scenarios into
one test.

- [ ] Write a test that initiates a ZarinPal payment, then asserts the request JSON sent and the redirect URL returned are both correct.
- [ ] Write a test with `Currency=IRR` and one with `Currency=IRT`, then assert both convert the order amount correctly.
- [ ] Write a test for verify code `100`, then assert the payment is marked succeeded.
- [ ] Write a test for verify code `101` with a matching prior success reference, then assert it reconciles as success.
- [ ] Write a test for verify code `101` with a non-matching reference, then assert it fails closed.
- [ ] Write a test for callback `Status != OK` (decline), then assert verify is never called and the payment fails.
- [ ] Write a test for any other verify error code, then assert the payment fails.
- [ ] Write a test with a mismatched authority or mismatched amount at verify time, then assert the payment fails and the order is not marked `Paid`.
- [ ] Write a test that tampers with the signed state value, then assert the callback is rejected.
- [ ] Write a test that uses an expired signed state value (past the 30-minute lifetime), then assert the callback is rejected.
- [ ] Write a test that sends the same successful callback twice, then assert the second call is idempotent (no double side effects).
- [ ] Write a test that simulates a timeout calling ZarinPal, then assert the attempt stays `Initiated` (not `Paid`, not `Failed`).
- [ ] Write a test where a late success verification arrives for an order already `Cancelled`, then assert the order does not become `Paid`.
- [ ] Write a test that misconfigures a Production host/URL (not on the allowlist or not HTTPS), then assert startup/activation fails.
- [ ] Write a test that inspects response bodies and log fixtures after a full flow, then assert no secrets (merchant ID, card PAN, etc.) appear in either.
- [ ] Write a test that stores a ZarinPal authority on an attempt, then asserts it persisted in the existing `GatewayReference` column and that the `RefId` from verify persisted separately in `ProviderReference`.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body, state the **exact host** of the `GatewayBaseUrl` you configured
(for example the host part of `Shop:Payments:ZarinPal:GatewayBaseUrl`) for each
environment. `F062` builds a client-side redirect allowlist and must be
configured with exactly these hosts — if the two disagree, every production
payment redirect is rejected by the browser client. Do not make `F062` guess
the host from ZarinPal's public documentation.

In the PR body also give `F062` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

Run these from the repository root, in this order, and fix every failure
before moving to the next command.

On the reference WSL setup there is no Linux `dotnet` binary — use `dotnet.exe`
instead of `dotnet` in every command below. See
`docs/architecture.md#local-development-environment-wsl--windows-net-sdk`.

The integration tests start PostgreSQL through Testcontainers, so Docker must
be running before you run any test command. Start it with `docker compose up -d postgres`
if Docker Desktop is not already up (the compose service is not what the tests
connect to, but it confirms the Docker daemon is reachable).

1. Build everything:

   ```bash
   dotnet build TenantForge.sln --nologo
   ```

2. Run only this task's Shop integration tests first (replace
   `<ShopTestClass>` with the exact class name you added or extended, for
   example `ShopCatalogAdminIntegrationTests`):

   ```bash
   dotnet test TenantForge.sln --nologo --filter FullyQualifiedName~<ShopTestClass>
   ```

3. Run the full test suite and confirm it is green:

   ```bash
   dotnet test TenantForge.sln --nologo
   ```

4. This task adds **no** EF migration (see "Do this in order" step 10).
   Confirm that no new file appeared under
   `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
   that `ShopDbContextModelSnapshot.cs` is unchanged. If either changed, you
   went outside this task's scope — revert it.

5. Re-read `docs/modules/SHOP.md` and check every routes/entities/config/auth/tests
   statement against the code you actually delivered. Then finish the
   `docs/learning/B045-<slug>.md` learning note.

6. Confirm the frontend was not touched:

   ```bash
   git diff --name-only origin/main... -- src/web
   ```

   This must print nothing.

## Non-goals

Refund, inquiry scheduler, multiple live merchant accounts, webhook, split payment or fee calculation.

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   grouped as: production code, EF migration (generated), tests,
   documentation. Say which files are generated rather than hand-written.
2. **Implementation decisions.** Every decision this Spec left to you, with
   the option you picked and one sentence of why. If you followed an "if
   unsure, do X" default from this Spec, say so and name it.
3. **Commands executed.** Every command from "Validation" above, copied
   verbatim in the order you ran them.
4. **Results of those checks.** For each command: pass or fail, and for the
   test commands the actual passed/failed/skipped counts. If you had to re-run
   something after a fix, say that and give the final result. Never report a
   command as passing if you did not run it.
5. **Risks, blockers and follow-up.** Anything you could not verify, any
   scenario from "Integration tests required" you could not cover and why, any
   contract detail that differed from this Spec, and anything the next task
   (F062) must know. Write "None." if there is genuinely nothing.
6. **Documentation impact statement.** The exact line
   `SHOP.md impact: <what you updated>` or
   `SHOP.md impact: none — <specific reason>`, plus the same line for
   `IAM.md`, `BuildingBlocks docs` and `IAM Contract docs` if your diff touched
   any of them (see `AGENTS.md`). A vague "docs not needed" is not accepted.

## Acceptance checklist

- [ ] The visible outcome works through `F062` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
