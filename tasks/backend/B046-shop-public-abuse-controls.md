# B046 — Rate-limit sensitive anonymous Shop flows

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S42`.
- Depends on: `B045, B043`.
- Immediate browser consumer: `F063`. Do not widen this API for an unnamed future screen.
- Visible outcome: Order lookup, cart mutation, order creation and payment initiation resist basic enumeration and request floods with predictable 429 responses.

## Do this in order

Before anything else, do the boilerplate from `AGENTS.md` and the Ownership
section above: read `AGENTS.md`, read the `B046` row in `tasks/TASKS.md`,
read this whole Spec, read `docs/modules/SHOP.md` if it exists, read
`tasks/slices/042-shop-public-abuse-controls.md`, read the matching section
of `docs/design/shop/http-contracts.md`, follow the branch-naming rule
(`backend/b046-<slug>`) and the ledger-update rule (only the `B046` row
changes status) already described above. Then follow these steps. They cover
the same ground as the "Required implementation" and "Required code shape"
sections below in a stricter order — read those sections too, they contain
detail not repeated here.

1. Confirm baseline commit `34dc44e` still matches current code (same
   baseline description as B044/B045: one Shop module project, internal EF
   entities, TSID IDs, separate migration-history table, raw-SQL IAM checks,
   anonymous storefront/cart/checkout/order/payment/lookup routes, exact
   tests under `Shop*IntegrationTests.cs`). This task adds no new routes —
   it only wraps existing anonymous routes with rate-limit policies.
2. Add a new options class `ShopRateLimitOptions` with a per-minute limit for
   each policy and a queue length of exactly zero (queue length zero means:
   once the limit is hit, reject immediately — do not queue/delay extra
   requests). Development default values, exactly:
   - lookup: 30 per minute
   - cart mutations: 120 per minute
   - checkout/order: 30 per minute
   - payment initiation: 20 per minute
   In Production, every one of these values must be configured explicitly to
   a positive number, and each must be within a documented maximum you write
   down in `docs/modules/SHOP.md`. If Production is missing any of these
   values, startup must fail.
3. Register the limiter in `src/api/TenantForge.Api/Program.cs`. As delivered
   today that file contains **no** `AddRateLimiter` call and **no**
   `UseForwardedHeaders` call — check this yourself before you start, then:
   - Add exactly one `builder.Services.AddRateLimiter(...)` call, placed after
     `builder.Services.AddShopModule(builder.Environment);` and before
     `var app = builder.Build();`. Never add a second `AddRateLimiter` call.
   - Add `app.UseForwardedHeaders(...)` as the first middleware, before the
     existing `app.UseCors();` line, configured with the trusted-proxy
     allowlist from the next bullet. This middleware does not exist yet; this
     task adds it.
   - Add `app.UseRateLimiter();` immediately after `app.UseCors();` and before
     `await app.UseIamModuleAsync();` — module activation is what maps the
     endpoints, so the limiter must be in the pipeline before it runs.
   - Partition rate-limit buckets by normalized tenant ID plus remote IP
     (i.e., the limiter tracks "this tenant + this IP" as one bucket, not
     tenant alone or IP alone).
   - When the app is behind a reverse proxy, only trust forwarded headers
     (like `X-Forwarded-For`) from an explicit configured allowlist of
     proxy IPs/networks. For any request not coming through a trusted proxy,
     use the direct remote IP instead of a forwarded header.
4. Define the exact policy names from "Required code shape" below in a
   static class `ShopRateLimitPolicies`, and apply them to routes exactly as
   follows:
   - `OrderLookup` policy → order lookup route.
   - `CartMutation` policy → cart create/add/update/delete routes.
   - `CheckoutOrder` policy → checkout summary route and order creation
     route.
   - `Payment` policy → payment initiation route.
   - Do **not** apply any IP-based rate limit to the ZarinPal provider
     callback route. Reason: many legitimate ZarinPal callbacks can share
     the same provider egress IP address, and B045 already protects that
     route with signed state, bounded input size, and idempotent
     verification.
   - Public catalog read routes are not rate-limited in this task.
5. When a limit is exceeded, return an RFC7807 (RFC7807 = the repo's
   standard JSON error body shape for HTTP errors) 429 response with:
   - `type` set to exactly `shop_rate_limit`,
   - a generic, Persian-safe detail message (a message that does not leak
     which specific resource, phone number, tracking code, cart, or order
     triggered the limit),
   - an integer `Retry-After` value.
   Never let the 429 body or headers reveal whether a specific tracking
   code, phone number, cart, or order actually exists.
6. Add request body size limits for: media uploads, JSON Shop request
   bodies, and callback query string length. Reject oversized requests
   before any model binding/validation work runs (fail fast, before spending
   CPU parsing something you're going to reject anyway).
   Logging for this task must include the policy name and tenant ID, and
   must never include phone numbers, tracking codes, coupon codes, or the
   ZarinPal authority value.
7. Write an EF migration only if this task introduces a persisted schema
   change (most rate-limiting is in-memory/middleware-only — if nothing
   persisted changes, skip this step, but say so explicitly in your plan).
8. Write every integration test listed in "Integration tests required"
   below.
9. Update `docs/modules/SHOP.md` (including the Production maximum values
   from step 2) and the matching section of
   `docs/design/shop/http-contracts.md`.
10. Write the `docs/learning/B046-<slug>.md` learning note per `AGENTS.md`.
11. Run every command in "Validation" below, in order, and fix failures
    before presenting the final diff for approval.
12. Work through the "Acceptance checklist" at the end of this file item by
    item before asking for final approval.

## Read before editing

Read `AGENTS.md`, the `B046` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/042-shop-public-abuse-controls.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Baseline as of commit `34dc44e` (later commits changed only `src/web/**` and
`tasks/**`, so this still describes the backend you will find): Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

Shop rate-limit options/policies, module registration and endpoint metadata, `src/api/TenantForge.Api/Program.cs` middleware placement, appsettings, tests, SHOP handbook and learning note. Backend mentor owns the shared host/config edits for this task.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

No new routes. Apply named ASP.NET rate-limiter policies to existing anonymous routes.

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = the repo's standard JSON error body shape for HTTP errors; reuse the existing helper, do not invent a new error format). Malformed TSIDs (TSID = a sortable numeric string ID type — see `TenantForge.BuildingBlocks`) and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add `ShopRateLimitOptions` with per-minute limits and queue length zero. Development defaults: lookup 30, cart mutations 120, checkout/order 30, payment initiation 20. Production requires explicit positive values within documented maxima.
2. Add the single `AddRateLimiter` registration and the `UseForwardedHeaders`/`UseRateLimiter` middleware to `src/api/TenantForge.Api/Program.cs` at the exact positions named in "Do this in order" step 3; Shop contributes named policies. Partition by normalized tenant ID plus remote IP. When behind a proxy, trust forwarded headers only from explicitly configured proxies/networks; otherwise use direct remote IP.
3. Apply lookup policy to order lookup, cart policy to create/add/update/delete, order policy to checkout summary/order creation, and payment policy to initiation. Do not IP-throttle the provider callback: many legitimate callbacks may share provider egress addresses, while B045 already protects that route with signed state, bounded input and idempotent verification. Public catalog reads are not limited in this slice.
4. Return RFC7807 429 with `type=shop_rate_limit`, generic Persian-safe detail and integer `Retry-After`. Do not disclose whether tracking code, phone, cart or order exists.
5. Bound request body sizes for media, JSON Shop requests and callback query lengths. Reject over-limit before model work. Logging includes policy and tenant but no phone/tracking/coupon/authority.

## Required code shape

Implement every member below completely — do not leave any of it as a
placeholder or a "TODO" comment. The names must match exactly.

```csharp
internal static class ShopRateLimitPolicies
{
    internal const string OrderLookup = "shop-order-lookup";
    internal const string CartMutation = "shop-cart-mutation";
    internal const string CheckoutOrder = "shop-checkout-order";
    internal const string Payment = "shop-payment";
}
// Endpoint mapping example:
endpoints.MapPost("/api/shop/{tenantId}/orders/lookup", handler)
    .RequireRateLimiting(ShopRateLimitPolicies.OrderLookup);
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

- [ ] Write a test for each of the four policies (`OrderLookup`, `CartMutation`, `CheckoutOrder`, `Payment`) that sends requests up to the configured limit, then asserts they succeed, then sends one more and asserts it gets a 429.
- [ ] Write a test that sends requests from two different tenant+IP partitions at the same time, then asserts one partition hitting its limit does not affect the other partition's remaining quota.
- [ ] Write a test that triggers a 429 for a valid lookup and a separate 429 for an invalid/nonexistent lookup, then asserts the `Retry-After` value and response body are identical in both cases.
- [ ] Write a test that sends a forwarded-for header from an untrusted source, then asserts it is ignored and the direct remote IP is used for partitioning instead.
- [ ] Write a test that starts the app in Production with a missing rate-limit configuration value, then asserts startup fails.
- [ ] Write a test that triggers a rate-limit rejection, then asserts the logs contain policy name and tenant ID but never phone number, tracking code, coupon code, or gateway authority.
- [ ] Write a test that runs the normal (non-abuse) integration test suite, then asserts it is not globally throttled by the new limiter (i.e., normal test traffic still passes).

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F063` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

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

4. This task adds **no** EF migration. Confirm that no new file appeared under
   `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
   that `ShopDbContextModelSnapshot.cs` is unchanged. If either changed, you
   went outside this task's scope — revert it.

5. Re-read `docs/modules/SHOP.md` and check every routes/entities/config/auth/tests
   statement against the code you actually delivered. Then finish the
   `docs/learning/B046-<slug>.md` learning note.

6. Confirm the frontend was not touched:

   ```bash
   git diff --name-only origin/main... -- src/web
   ```

   This must print nothing.

## Non-goals

CAPTCHA, WAF, distributed Redis counters, bot scoring, catalog caching or account lockout.

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
   (F063) must know. Write "None." if there is genuinely nothing.
6. **Documentation impact statement.** The exact line
   `SHOP.md impact: <what you updated>` or
   `SHOP.md impact: none — <specific reason>`, plus the same line for
   `IAM.md`, `BuildingBlocks docs` and `IAM Contract docs` if your diff touched
   any of them (see `AGENTS.md`). A vague "docs not needed" is not accepted.

## Acceptance checklist

- [ ] The visible outcome works through `F063` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
