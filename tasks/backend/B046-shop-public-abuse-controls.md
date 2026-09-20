# B046 — Rate-limit sensitive anonymous Shop flows

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S42`.
- Depends on: `B045, B043`.
- Immediate browser consumer: `F063`. Do not widen this API for an unnamed future screen.
- Visible outcome: Order lookup, cart mutation, order creation and payment initiation resist basic enumeration and request floods with predictable 429 responses.

## Read before editing

Read `AGENTS.md`, the `B046` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/042-shop-public-abuse-controls.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

Shop rate-limit options/policies, module registration and endpoint metadata, `src/api/TenantForge.Api/Program.cs` middleware placement, appsettings, tests, SHOP handbook and learning note. Backend mentor owns the shared host/config edits for this task.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

No new routes. Apply named ASP.NET rate-limiter policies to existing anonymous routes.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add `ShopRateLimitOptions` with per-minute limits and queue length zero. Development defaults: lookup 30, cart mutations 120, checkout/order 30, payment initiation 20. Production requires explicit positive values within documented maxima.
        2. Register one `AddRateLimiter` call at host composition only if not already present; Shop contributes named policies. Add `app.UseRateLimiter()` after forwarded-header processing/CORS and before module endpoint execution is reached. Partition by normalized tenant ID plus remote IP. When behind a proxy, trust forwarded headers only from explicitly configured proxies/networks; otherwise use direct remote IP.
        3. Apply lookup policy to order lookup, cart policy to create/add/update/delete, order policy to checkout summary/order creation, and payment policy to initiation. Do not IP-throttle the provider callback: many legitimate callbacks may share provider egress addresses, while B045 already protects that route with signed state, bounded input and idempotent verification. Public catalog reads are not limited in this slice.
        4. Return RFC7807 429 with `type=shop_rate_limit`, generic Persian-safe detail and integer `Retry-After`. Do not disclose whether tracking code, phone, cart or order exists.
        5. Bound request body sizes for media, JSON Shop requests and callback query lengths. Reject over-limit before model work. Logging includes policy and tenant but no phone/tracking/coupon/authority.


## Required code shape

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

each policy allows up to limit then 429; separate tenant/IP partitions; Retry-After/body identical for valid/invalid lookup; forwarded header ignored unless trusted; Production missing config fails; logs redact sensitive values; normal integration suite not globally throttled.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F063` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

CAPTCHA, WAF, distributed Redis counters, bot scoring, catalog caching or account lockout.

## Acceptance checklist

- [ ] The visible outcome works through `F063` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
