# B046 — Rate-limit sensitive anonymous Shop flows

## 1. Files changed and why

**Production code**

- `src/modules/shop/TenantForge.Modules.Shop/features/rateLimiting/ShopRateLimitOptions.cs`
  (new) — the `Shop:RateLimiting` options type: the four per-minute limits,
  `MaxRequestBodyBytes`, `QueueLength`, the size-bound constants, the
  Development defaults, and the Production fail-closed `Bind`/`Validate`.
- `src/modules/shop/TenantForge.Modules.Shop/features/rateLimiting/ShopRateLimiterHostExtensions.cs`
  (new) — the single registration seam (`AddShopRateLimiter`, the host's only
  `AddRateLimiter` call) and the body-size guard (`UseShopRequestBodySizeLimit`),
  plus the `internal` `ShopRateLimitPolicies` names and the generic `429` handler.
- `src/modules/shop/TenantForge.Modules.Shop/ShopConfig.cs` — `ValidateConfiguration`
  now binds and validates `ShopRateLimitOptions` (fail closed).
- `src/api/TenantForge.Api/Program.cs` — host wiring: `AddShopRateLimiter` after the
  module registrations, `ForwardedHeadersOptions` from `ForwardedHeaders`, and the
  middleware order `UseForwardedHeaders` → `UseCors` → `UseRateLimiter` →
  `UseShopRequestBodySizeLimit` → module activation.
- Feature files — `.RequireRateLimiting(…)` added to the five anonymous routes
  (`OrderLookupFeature`, `CartsFeature` ×4 mutations, `CheckoutFeature`,
  `OrderCreationFeature`, `PaymentsFeature` initiate).
- `ZarinPal/ZarinPalCallbackFeature.cs` — the callback handler now receives the
  `HttpContext` and rejects an over-bound query string with a generic `413` before
  parsing.

**EF migration (generated):** none. Nothing persisted changed; the limiter is
in-memory and the guards are middleware.

**Tests**

- `tests/integration/TenantForge.Api.IntegrationTests/ShopRateLimitIntegrationTests.cs`
  (new) — the 12 B046 facts plus a dedicated `ShopRateLimitDbFixture`/collection
  and two purpose-built `WebApplicationFactory`s.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs` — the new
  `ShopRateLimitDbFixture` + `ShopRateLimitIsolatedCollection`.
- `ApiFactory.cs`, `ProductionFailClosedTests.cs`, `ShopZarinPalPaymentIntegrationTests.cs`,
  `ShopPaymentLifecycleIntegrationTests.cs` — existing test hosts given explicit
  `Shop:RateLimiting:*` values so the new Production requirement does not change
  the failure those hosts were built to assert.

**Documentation** — `docs/modules/SHOP.md`, `docs/design/shop/http-contracts.md`
(S42), `docs/knowledge/AGENT-backend.md`, `docs/knowledge/HUMAN-backend.md`, this
note.

## 2. Request flow from endpoint to response

For a rate-limited route (e.g. `POST /api/shop/{tenantId}/orders/lookup`):

1. `UseForwardedHeaders` runs first and, if the request arrived through a proxy
   on the `ForwardedHeaders` allowlist, rewrites `Connection.RemoteIpAddress`
   from `X-Forwarded-For`; otherwise the direct connection IP is kept.
2. `UseRateLimiter` sees the route's `RequireRateLimiting("shop-order-lookup")`
   metadata, asks the `shop-order-lookup` policy for a partition, and runs that
   policy's per-request lambda. The lambda reads the route `tenantId` and the
   (already-resolved) remote IP and builds the key `"{tenantId.ToLower()}|{ip}"`.
3. `RateLimitPartition.GetFixedWindowLimiter<string>(key, …)` looks up (or creates)
   a one-minute fixed-window limiter for that exact key, with `PermitLimit` from
   `ShopRateLimitOptions` and `QueueLimit = 0`.
4. If a permit is available the request proceeds to the handler. If not,
   `OnRejected` (`WriteRejectionAsync`) writes the one generic `429`:
   `application/problem+json`, `type: "shop_rate_limit"`, integer `Retry-After: 1`,
   matching integer `retryAfter` in the body, and logs `policy` + `tenantId` only.
   The handler never runs, so nothing tenant-specific is ever computed or leaked.

For an oversized body: `UseShopRequestBodySizeLimit` (a plain middleware after
`UseRateLimiter`) checks the Shop-scoped `Content-Length` against
`MaxRequestBodyBytes` (JSON) or the 5 MiB ceiling (anything else) and answers a
generic `413 shop_request_too_large` before model binding. The ZarinPal callback
checks its query-string length the same way (`413 shop_callback_too_large`).

## 3. Backend concepts introduced

- **Per-policy, per-key fixed-window rate limiting** on .NET 10: the policy
  lambda computes a key per request, and the framework keeps one windowed limiter
  per distinct key. No global limiter; only the routes that opt in are limited.
- **The partition key is a tuple of two server-known facts** (normalized tenant
  id + effective remote IP), not a client-supplied value — so a caller cannot
  choose its own bucket.
- **Trusted-proxy resolution** via `UseForwardedHeaders`: forwarded headers are
  honored only from an explicit allowlist; an untrusted source's
  `X-Forwarded-For` is ignored and the direct IP is used.
- **`IOptions<T>` lazy bind**: the options `Configure` delegate runs the first
  time `.Value` is read — here during the body-size middleware's pipeline setup,
  before the module's own `ValidateConfiguration` — so a missing Production value
  surfaces from the bind, not the module check.
- **RFC 7807 429 as an anti-enumeration answer**: one identical body for valid and
  invalid inputs, so existence is never observable from a rejection.

## 4. Important security decisions

- **Default-deny / fail closed**: in Production a missing or out-of-range
  rate-limit value fails startup; the Development defaults are never used silently
  outside Development. `QueueLength != 0` is also refused.
- **No enumeration oracle**: the `429` body and headers name no tenant, cart,
  order, phone, tracking code, coupon or authority, and the valid-vs-invalid
  responses are byte-identical.
- **Logs carry only stable identifiers**: the rejection log is `policy` +
  `tenantId`; phone/tracking/coupon/authority never appear.
- **Fail fast on size**: oversized bodies and the callback query are rejected
  before any model binding/validation, so an attacker cannot spend CPU parsing a
  payload that will be dropped.
- **Provider callback not IP-limited**: many legitimate ZarinPal callbacks share
  one egress IP, and B045 already bounds it (signed state, bounded input,
  idempotent verify). Public catalog reads are also not limited in this slice.

## 5. Alternatives deliberately postponed

- **Distributed (Redis) counters** — in-process buckets are enough for one
  deployable node; a scale-out that would double per-IP budgets is a non-goal here.
- **Sliding-window / token-bucket limiters** — a fixed per-minute window with
  queue length zero is the Spec's chosen behavior; simpler and immediately testable.
- **CAPTCHA, WAF, bot scoring, catalog caching, account lockout** — explicit
  non-goals for this task.
- **`RequireMaxRequestBodySize` per-endpoint** — not in the .NET 10 surface used
  here; a host-level Shop-scoped middleware gives the same fail-fast bound with
  one configuration value.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopRateLimitIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Manual demo (Development host):

```bash
docker compose up -d postgres
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run \
  --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

- Create a cart for a tenant and `POST /api/shop/{tenantId}/orders/lookup` 30
  times in a row (the Development default); the 31st returns `429` with
  `Retry-After: 1` and body `{"type":"shop_rate_limit",…,"retryAfter":1}`.
- Send the same lookup from a second tenant in the same burst: the second tenant
  still succeeds once the first is 429'ing (independent buckets).
- Send an `X-Forwarded-For: 1.2.3.4` header with no trusted proxy configured: the
  limit still applies against your real IP (the header is ignored).
- Post a JSON body larger than 512 KiB to a Shop route: generic `413
  shop_request_too_large`.
- `GET /api/shop/{tenantId}/payments/zarinpal/callback?state=<16001+ chars>`:
  generic `413 shop_callback_too_large`.

## 7. Three review questions for the learner

1. Why must `UseForwardedHeaders` run **before** the rate limiter, and what would
   break if a request that did not come through a trusted proxy could set
   `X-Forwarded-For` to anything it wanted?
2. The `429` body is the same for a real order's tracking code and a fabricated
   one. What would a caller be able to learn if the over-limit response for a
   *valid* pair were different (e.g. `429` vs `404`), and why is that a problem?
3. `IOptions<ShopRateLimitOptions>.Value` is first read by the body-size middleware
   during pipeline setup, not by `ShopConfig.ValidateConfiguration`. Explain why a
   missing `Shop:RateLimiting` value in Production throws before the module's own
   validation runs, and why that is acceptable.
