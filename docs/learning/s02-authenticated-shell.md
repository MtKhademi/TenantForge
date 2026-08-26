# B002 — S02 current account and logout: learning note

## Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj`
  Added the `Microsoft.AspNetCore.Authentication.JwtBearer` package. `JwtSecurityTokenHandler` (used to *issue* tokens in B001) came from `System.IdentityModel.Tokens.Jwt`, but *validating* tokens in the request pipeline needs the ASP.NET Core `JwtBearer` handler, which is a separate package.
- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`
  Restructured `RegisterServices` around a key insight: **authentication/authorization services are environment-independent, but the development *credential source* is not.**
  - `services.AddAuthorization()` runs in every environment — protected endpoints (`RequireAuthorization`) need the authorization services and the 401 challenge behavior no matter what.
  - `AddAuthentication(...).AddJwtBearer(...)` also runs in every environment, so the pipeline has a registered scheme (and therefore a working 401 challenge) in Production too.
  - The only thing that stays Development-gated is the *login shortcut*: `ICredentialChecker` + `DevelopmentJwtIssuer`. Outside Development those simply do not exist, so login is impossible — fail closed.
  - The signing key is **not** registered statically. It is assigned at runtime (see `DevelopmentJwtBearerOptions`), so a late configuration source can supply it and — critically — in non-Development its *absence* degrades to "no valid key" instead of a startup or request-time crash.
- `src/modules/iam/.../features/login/DevelopmentJwtBearerOptions.cs` (new)
  An `IPostConfigureOptions<JwtBearerOptions>` that assigns `TokenValidationParameters.IssuerSigningKey` from the lazily-bound `DevelopmentLoginOptions`. When the key is missing (always, outside Development, because startup validation forbids the section there) it substitutes a random, per-process key. That key matches nothing, so every token fails signature validation → clean `401`, instead of a `500` from "no signing key configured."
- `src/modules/iam/.../features/account/CurrentAccountResponse.cs` (new)
  The response DTO for `GET /api/auth/me`, matching the S02 contract field-for-field.
- `src/modules/iam/.../features/account/CurrentAccountFeature.cs` (new)
  The protected endpoint. Maps only the server-validated claims (`sub`, `email`, `name`, `isPlatformAdmin`) onto the response and returns `401` if the caller is not authenticated or any required claim is missing (default deny). Declares `.RequireAuthorization()`.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs`
  `MapIamModule` now also calls `MapCurrentAccountFeature()`. The public seam is unchanged in shape — the host still only calls `AddIamModule` / `ValidateIamModuleConfiguration` / `MapIamModule`.
- `src/api/TenantForge.Api/Program.cs`
  Added `app.UseAuthentication()` and `app.UseAuthorization()` **unconditionally**, in that order, after `UseCors()`. No environment gate, because the services now exist in every environment (see above).
- `tests/integration/.../CurrentAccountIntegrationTests.cs` (new)
  Seven integration tests covering valid, missing, malformed, expired, forged, wrong-issuer/audience, and Production fail-closed behavior. Includes an internal `TestJwtFactory` to mint tokens (including expired ones) directly.
- `docs/learning/s02-authenticated-shell.md` — this note.

## Request flow (endpoint to response)

`GET /api/auth/me`:

1. Request hits `UseAuthentication()`. The registered `JwtBearer` handler reads the `Authorization: Bearer <token>` header.
2. The handler validates: signature (against the key assigned by `DevelopmentJwtBearerOptions`), issuer/audience (`TenantForge`), and lifetime (`exp`/`nbf`, 30 s skew). If any check fails it marks the caller as *challenged* but not authenticated.
3. Request hits `UseAuthorization()`. Because the endpoint has `RequireAuthorization()` and the caller is unauthenticated, the authorization middleware invokes its default failure handler, which calls `ChallengeAsync` on the default scheme → **`401`**. The request never reaches the handler body.
4. Only a fully valid token reaches the handler body. There, `principal.Identity.IsAuthenticated` is re-checked (defense in depth) and the four claims are read. Any missing/blank claim or a non-true `isPlatformAdmin` also returns **`401`**.
5. Success returns `200` with the exact contract JSON, built *only* from the validated claims — never from the request body or query.

## Backend concepts introduced

- **Authentication vs. authorization as two distinct middleware steps.** `UseAuthentication()` turns a token into a `ClaimsPrincipal`; `UseAuthorization()` decides whether that principal may proceed. They are separate services (`AddAuthentication` vs `AddAuthorization`) and must run in order.
- **`RequireAuthorization()` endpoint metadata + the 401 challenge.** A protected endpoint does not manually check "am I logged in?" — the authorization middleware rejects unauthenticated callers with a challenge before the handler body runs. The default challenge for the Bearer scheme is `401` with a `WWW-Authenticate` header.
- **`ClaimsPrincipal` and claim validation.** The authenticated identity is a bag of claims the token handler produced *after* verifying the signature. The endpoint reads only the claims it needs and treats any missing one as "not really authenticated."
- **`IPostConfigureOptions<TOptions>` for lazy, runtime option assignment.** The signing key cannot be a static registration because it comes from configuration that is finalized after DI is built (and that test hosts inject later). A post-configure hook runs exactly when the options instance is first resolved, so it sees the fully-assembled configuration.
- **Fail-closed by absence, not by throwing.** In non-Development there is no development key. Instead of throwing (a `500`) when the key is missing, validation falls back to a random key that matches nothing — so the *only* possible outcome of an invalid/absent key is a `401`, never a crash.
- **Client logout vs. server-side revocation.** The slice deliberately adds *no* logout endpoint and no server-side session. Logout in this slice is a client operation: the SPA discards the stored token. Because tokens are stateless (JWT), the server cannot "invalidate" one without a server-side session or a revocation list — that is future work (a later slice), documented here rather than invented.

## Important security decisions

- **Identity comes only from validated token claims.** `/api/auth/me` never reads identity from the request body, query string, or any client-supplied field. A client cannot claim to be `development-admin` by sending `{"id":"development-admin"}` — the server derives identity exclusively from a token whose signature it verified.
- **Signature validation is the real gate.** The "wrong key" and "wrong issuer/audience" tests prove that a structurally perfect token (correct claims, correct shape, valid signature *for a different key*) is rejected. This is what stops a client from minting its own tokens.
- **Default deny on any missing claim.** If a token were somehow authenticated but missing `sub`/`email`/`name`, or had `isPlatformAdmin` not exactly `true`, the endpoint returns `401` rather than echoing a partial/empty identity.
- **Fail closed outside Development.** In Production the development login shortcut (checker + issuer) is simply not registered, and the JWT validation key is a random non-matching one — so both login and `/api/auth/me` return `401`. The startup check from B001 still throws if the `IAM:DevelopmentLogin` section is present outside Development, so the shortcut cannot be silently enabled in production.
- **No secrets in logs.** The pipeline does not log tokens, signing keys, or passwords. The existing B001 log-hygiene test still passes against the restructured registration.

## Alternatives deliberately postponed

- **Server-side logout / token revocation.** Would require a server-side session store or a revocation/deny list and a `DELETE /api/auth/logout` (or similar). The slice explicitly states logout is a client operation for now.
- **Refresh tokens and rotation.** Not needed until there is a persistent user and a real token-lifetime policy.
- **Persistent user / real identity store.** `sub`/`email`/`displayName` still come from the hardcoded development identity; a real user record arrives with B004/B005.
- **Authorization beyond "authenticated + platform admin."** No roles, permissions, or tenant scoping yet — that is later slices.
- **Rate limiting, audit logging of auth attempts, and token caching.** Future slices with visible consumers.

## How to verify

Prerequisite: .NET 10 SDK (this machine has no Linux `dotnet`; the Windows SDK is reachable as `dotnet.exe`).

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
# Focused:
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests \
  --filter "FullyQualifiedName~CurrentAccountIntegrationTests"
```

Live demo (Development — use `dotnet.exe run`, which forces Development):

```bash
# API on :5000
dotnet.exe run --project src/api/TenantForge.Api --urls http://0.0.0.0:5000

TOKEN=$(curl -s -X POST http://<host>:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}' \
  | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

curl -i http://<host>:5000/api/auth/me -H "Authorization: Bearer $TOKEN"   # 200 + identity
curl -i http://<host>:5000/api/auth/me                                      # 401
curl -i http://<host>:5000/api/auth/me -H "Authorization: Bearer not.a.jwt" # 401
```

Prove fail-closed in Production (built DLL, default environment — WSL env vars do *not* propagate into the Windows `dotnet.exe`, so just run the DLL with no override):

```bash
dotnet.exe build TenantForge.sln
# from src/api/TenantForge.Api, run the DLL with no ASPNETCORE_ENVIRONMENT (defaults to Production):
dotnet.exe bin/Debug/net10.0/TenantForge.Api.dll --urls http://0.0.0.0:5001
curl -i http://<host>:5001/api/auth/me        # 401 (not 500)
curl -i -X POST http://<host>:5001/api/auth/login ...  # 401, no checker
```

Note (this machine): a WSL-side `ASPNETCORE_ENVIRONMENT=Production dotnet.exe ...` is *not* seen by the Windows process; use `dotnet.exe run` (forces Development via `launchSettings.json`) or `cmd.exe /c "set X=Y && dotnet.exe ..."`. WSL `kill` cannot stop the Windows process — use `powershell.exe -Command "Stop-Process -Name dotnet -Force"`.

## Review questions

1. `GET /api/auth/me` returns `401` for an expired, forged, and wrong-issuer token alike. What is the *single* server-side check that makes all three impossible to forge, and why is "the token is a valid JWT" not enough?
2. Why is the JWT signing key assigned in an `IPostConfigureOptions` hook instead of being registered statically next to the other services? What breaks if you try to read it at registration time (hint: when is configuration finalized, and what do test hosts inject afterwards)?
3. This slice adds no logout endpoint and no server-side revocation. Explain why a stateless JWT cannot be "logged out" server-side today, and what concrete piece of server state would have to exist to make a real logout possible.
