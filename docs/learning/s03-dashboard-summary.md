# B003 — S03 dashboard summary API: learning note

## Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/AuthorizationPolicyNames.cs` (new)
  A small **internal** constants class holding the name of the authorization policy this slice registers (`PlatformAdmin`). Internal, because only features inside the IAM module map endpoints against this policy — the public `IamModule` seam is unchanged.
- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`
  `RegisterServices` now registers a **named authorization policy** on top of the plain `AddAuthorization()` from B002:

  ```csharp
  options.AddPolicy(AuthorizationPolicyNames.PlatformAdmin, policy =>
      policy.RequireClaim("isPlatformAdmin", "true"));
  ```

  Registered in **every** environment, exactly like `AddAuthorization()` itself: authorization is an environment-independent responsibility. Outside Development the policy simply can never be satisfied, because no valid token can be produced there (fail closed by absence, same principle as B002's signing-key substitution).
- `src/modules/iam/TenantForge.Modules.Iam/features/dashboard/DashboardSummaryResponse.cs` (new)
  Response DTO matching the S03 contract field-for-field: `environment`, `apiStatus`, `platformAdminCount`, `generatedAtUtc`.
- `src/modules/iam/TenantForge.Modules.Iam/features/dashboard/DashboardSummaryFeature.cs` (new)
  The protected query endpoint: `MapGet("/api/platform/dashboard-summary", ...)` + `.RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin)`. All values are computed honestly from the current process state (see "Security decisions" for why each is honest).
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs`
  One added line in `MapIamModule()`: `endpoints.MapDashboardSummaryFeature()`. The module's public seam (three calls from the host) is unchanged — that is the whole point of the seam pattern.
- `tests/integration/.../DashboardSummaryIntegrationTests.cs` (new)
  Four integration tests: 200 with the exact contract (including a real-UTC and timestamp-range assertion), 401 with no token, 403 with a valid token whose `isPlatformAdmin` claim is `"false"`, and Production fail-closed (401 even for a well-formed development token).
- `tests/integration/.../CurrentAccountIntegrationTests.cs`
  One additive change to the **internal** `TestJwtFactory.Issue`: an optional `bool isPlatformAdmin = true` parameter so tests can mint a valid *non-admin* token. The default keeps every existing test's behavior byte-identical.
- `tasks/TASKS.md` — active row lifecycle only (`planned → in_progress`, later `→ review` / `done` at delivery).
- `docs/learning/s03-dashboard-summary.md` — this note.

## Request flow (endpoint to response)

`GET /api/platform/dashboard-summary`:

1. Request hits `UseAuthentication()`. The `JwtBearer` handler (registered in B002) reads `Authorization: Bearer <token>` and validates signature, issuer/audience and lifetime. No header / invalid token → the principal stays unauthenticated.
2. Request hits `UseAuthorization()`. The endpoint carries the `PlatformAdmin` policy metadata.
   - Unauthenticated caller → the authorization middleware calls **`ChallengeAsync`** → **401** with `WWW-Authenticate: Bearer`.
   - Authenticated caller whose `isPlatformAdmin` claim is missing or not exactly `"true"` → the policy's claim requirement fails → the middleware calls **`ForbidAsync`** → **403**. The request never reaches the handler body in either case.
3. Only an authenticated platform administrator reaches the handler body. It reads `IHostEnvironment.EnvironmentName` (the real host environment) and `DateTimeOffset.UtcNow` and returns **200** with the four contract fields. No request body, query or client-supplied field is read for any value.

## Backend concepts introduced

- **Authentication vs. authorization produce different failure codes.** B002 answered "everything bad" with 401 because it only checked "is anyone there". This slice adds the *policy* step: the same middleware distinguishes **401 = who are you?** (challenge) from **403 = I know who you are, and no** (forbid). That distinction is what the S03 contract's "401 or 403 as appropriate" is asking for.
- **Named authorization policies (`AddPolicy` + `RequireClaim`).** A policy is a reusable, named set of requirements. `RequireClaim("isPlatformAdmin", "true")` means "the principal must have a claim of that type whose value equals that string". Note the string comparison: claim values are always strings, so the JWT must carry `"true"`, and anything else (`"false"`, `"True"`, `"1"`, missing) fails the requirement.
- **`.RequireAuthorization(policyName)` in Minimal APIs** — the policy name becomes endpoint metadata; the authorization middleware enforces it before the lambda runs. The endpoint body therefore contains zero authorization logic, which is exactly what you want: there is no second, drift-prone copy of the rule.
- **Honest computed values vs. constants.** `environment` and `generatedAtUtc` are real process state; `apiStatus` and `platformAdminCount` are constants *for now*, but constants that are still true for this stage (see below). The contract is stable; when B004/B005 introduce persistence, the constants become queries without any change to the response shape.

## Important security decisions

- **Default deny.** No claim, or a claim with any value other than the exact string `"true"`, is denied. A token missing `isPlatformAdmin` is rejected even though it is perfectly authenticated.
- **401 vs 403 are enforced by the middleware, not the handler.** Because the check lives in the authorization policy, it is impossible for a future edit to the handler body to accidentally bypass it — the 403 is produced before the handler executes.
- **Identity and values come only from the server.** The endpoint never reads identity from the request. A client cannot request a different `environment`, force `apiStatus`, or change `platformAdminCount` — none of those are inputs.
- **The values are honest for this stage, and the endpoint says nothing it does not know:**
  - `environment` — the actual `IHostEnvironment` name of the running host (test hosts report `Development`/`Production` accordingly).
  - `apiStatus = "Healthy"` — the API is reachable and able to execute this request; no dependency is probed yet, and claiming otherwise would be a lie. This is the same honesty standard as `/health`.
  - `platformAdminCount = 1` — there is exactly one platform administrator in the system at this stage: the single seeded development administrator from B001. No persistence exists, so there is nothing else to count. This constant must be revisited (replaced by a real query) in B004/B005.
- **Fail closed outside Development.** In Production the development signing key does not exist (startup validation forbids the section), so no token can validate; the endpoint answers 401 for everything, including structurally perfect development tokens. Covered by an integration test.
- **No secrets in logs.** The endpoint logs nothing; the existing B001 log-hygiene test still passes.

## Alternatives deliberately postponed

- **A real `platformAdminCount` query.** Requires persistence (B004) and the seeded admin (B005); until then the honest value is the constant 1.
- **Real health-check infrastructure** (`IHealthCheck` / `/health` probes) so `apiStatus` reflects dependencies. S03 explicitly forbids speculative infrastructure.
- **Caching of the summary.** Explicitly out of scope; the value changes every request (`generatedAtUtc`) anyway.
- **Role/permission policies beyond the single claim.** The full `Module.Resource.Action` permission catalog arrives in S09; until then "authenticated + `isPlatformAdmin=true`" is the whole authorization surface.
- **Refactoring `/api/auth/me` to use the same policy.** Its manual claim check predates this policy; it is behaviorally equivalent and was left untouched to keep the slice minimal.

## How to verify

This machine has no Linux `dotnet`; the Windows SDK is reached as `dotnet.exe`.

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests \
  --filter "FullyQualifiedName~DashboardSummaryIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests   # full suite
```

Live demo (Development; from WSL bind `0.0.0.0` and curl through the gateway IP from `ip route`):

```bash
dotnet.exe run --project src/api/TenantForge.Api --urls http://0.0.0.0:5000

GW=<gateway from: ip route>   # e.g. 172.30.176.1

curl -i http://$GW:5000/api/platform/dashboard-summary                  # 401
TOKEN=$(curl -s -X POST http://$GW:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
curl -i http://$GW:5000/api/platform/dashboard-summary \
  -H "Authorization: Bearer $TOKEN"                                     # 200 + exact contract
```

For a live **403**, mint a token signed with the development key whose `isPlatformAdmin` claim is `"false"` (the integration test does this via `TestJwtFactory.Issue(..., isPlatformAdmin: false)`).

## Review questions

1. The policy is `RequireClaim("isPlatformAdmin", "true")` — a string comparison. List the claim values that are **denied** and explain why `"1"`, `"True"` and a missing claim are each denied rather than "close enough".
2. Why does the handler body contain no claim checks at all, while `/api/auth/me` (B002) still re-checks its claims manually? Which of the two is harder to get wrong in a future edit, and why?
3. `platformAdminCount` is a hardcoded `1`. Explain why that is an *honest* value today but a *liar* tomorrow, and what concrete change in B004/B005 turns it into a real query without changing the response contract.
