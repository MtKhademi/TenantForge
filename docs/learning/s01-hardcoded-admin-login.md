# B001 — S01 development login: learning note

## Files changed and why

- `TenantForge.sln` — new solution binding the API host, the IAM module, and the integration tests.
- `src/api/TenantForge.Api/` — new Minimal API host. It composes the IAM module and registers CORS for the Vite dev origin; it contains no IAM business rules.
  - `appsettings.json` — base config (logging, allowed hosts), no credentials.
  - `appsettings.Development.json` — development-only credentials + dev JWT signing key under the `IAM:DevelopmentLogin` section, allowed CORS origin.
  - `Properties/launchSettings.json` — binds `http://localhost:5000` in `Development` so the front clone (F002) can reach the API.
  - `Program.cs` — startup: CORS, `AddIamModule`, the post-Build `ValidateIamModuleConfiguration` check, then `MapIamModule`.
  - `HealthEndpoints.cs` — `GET /health` probe used by the front app to distinguish "API unavailable" from "invalid credentials".
- `src/modules/iam/TenantForge.Modules.Iam/` — new IAM module class library (vertical slice layout per `docs/architecture.md`).
  - `IamModule.cs` — the module's public composition seam: `AddIamModule` (DI), `ValidateIamModuleConfiguration` (fail-closed check), `MapIamModule` (endpoint mapping). The API host depends only on this class; every feature inside the module is `internal`. It holds exactly one `IModuleConfig` (`IAMConfig`) and delegates to it.
  - `IModuleConfig.cs` — contract every module config class implements: a `SectionName` (the top-level config tag, `IAM`), `RegisterServices` (add services to the container) and `ValidateConfiguration` (check the config and fail startup when it is not correct).
  - `IAMConfig.cs` — the IAM module's config: automatically reads the `IAM` section from `IConfiguration`; in `Development` it registers the credential checker + JWT issuer (options bound lazily from `IAM:DevelopmentLogin`); outside `Development` it registers nothing; and in `ValidateConfiguration` it throws when the `IAM:DevelopmentLogin` section is present outside Development (fail closed) or incomplete in Development.
  - `features/login/` holds the login slice (all `internal`):
  - `LoginRequest.cs` / `LoginResponse.cs` — DTOs matching the fixed S01 contract field-for-field.
  - `CredentialChecker.cs` — `ICredentialChecker` abstraction so the development checker can be swapped for a real identity store (B004) without touching the endpoint.
  - `DevelopmentLoginOptions.cs` — options record for the `IAM:DevelopmentLogin` section.
  - `DevelopmentCredentialChecker.cs` — fixed-time comparison of the configured development email/password.
  - `DevelopmentAdminIdentity.cs` — constants for the single development-admin identity, token lifetime, issuer/audience.
  - `DevelopmentJwtIssuer.cs` — creates the signed JWT with only the claims current slices need.
  - `LoginFeature.cs` — internal slice implementation: `MapLoginFeature` (endpoint mapping only; registration/validation live in `IAMConfig`).
- `tests/integration/TenantForge.Api.IntegrationTests/` — xUnit + `WebApplicationFactory` integration tests: `ApiFactory.cs` (test host with in-memory config + log capture), `LoginIntegrationTests.cs`, `ProductionFailClosedTests.cs`.
- `docs/learning/s01-hardcoded-admin-login.md` — this note.

## Request flow (endpoint to response)

1. Browser (F002, next task) sends `POST /api/auth/login` with a JSON body.
2. `Program.cs` registers the endpoint via `app.MapIamModule()` → `IamModule` → internal `MapLoginFeature`; `MapPost` binds the JSON body to the `LoginRequest` record.
3. Missing/blank email or password → `400` ProblemDetails (`application/problem+json`).
4. The handler resolves `ICredentialChecker` via `[FromServices]`. In `Development` this is `DevelopmentCredentialChecker` (fixed-time comparison against `IAM:DevelopmentLogin:Email`/`:Password`); in any other environment nothing is registered, so login is impossible — fail closed.
5. On success the handler resolves `DevelopmentJwtIssuer`, which builds a `JwtSecurityToken` (HS256, dev signing key) containing only `sub`, `email`, `name`, `isPlatformAdmin` plus standard `iss`/`aud`/`nbf`/`exp`.
6. Success returns `200 OK` with the fixed contract JSON (`accessToken`, `expiresAtUtc`, `user`).
7. Any credential mismatch returns `401` ProblemDetails with a generic title/detail that never says which field was wrong and never echoes the submitted values.

Startup flow: `builder.Build()` → `IamModule.ValidateIamModuleConfiguration` → `IAMConfig.ValidateConfiguration` inspects the *fully assembled* configuration. If the environment is not `Development` and the `IAM:DevelopmentLogin` section is present it throws `InvalidOperationException` (fail closed); in `Development` the section must be complete (Email, Password, SigningKey) or startup fails the same way. Registration itself (`IAMConfig.RegisterServices`) runs earlier in `AddIamModule`, but it only *adds* services — it never decides pass/fail, because `WebApplication.CreateBuilder` has not yet consumed every configuration source at registration time (and `WebApplicationFactory` test configuration is injected afterwards). A check done too early would silently pass on a config that is wrong once fully assembled.

## Backend concepts introduced

- **Modular monolith composition** — the host composes modules through one public seam (`IamModule.AddIamModule` / `ValidateIamModuleConfiguration` / `MapIamModule`); everything else in the module is `internal`, so the API host cannot reach into feature details.
- **Module config pattern** — `IModuleConfig` declares a module's config contract (`SectionName`, `RegisterServices`, `ValidateConfiguration`); `IAMConfig` binds the `IAM` section from `IConfiguration`, checks it, and throws when it is not correct before anything is relied on. This keeps "read + validate + register" for a module in one class instead of scattered `AddOptions` calls.
- **Minimal API endpoint mapping** (`MapPost`) with a typed request record and `Results.*` return values.
- **`[FromServices]` parameter injection** in Minimal APIs, including nullable optional services (`ICredentialChecker?`, `DevelopmentJwtIssuer?`) so the same endpoint code serves both Development and non-Development hosts.
- **Lazy configuration binding** — `DevelopmentLoginOptions` is resolved from `IConfiguration` at injection time (not captured at registration), so late-arriving configuration sources are still honored.
- **Environment-gated registration** — the development checker is only registered in `Development`.
- **JWT creation** with `JwtSecurityTokenHandler` + `SecurityTokenDescriptor`. Note: `System.IdentityModel.Tokens.Jwt` is not part of the shared ASP.NET Core framework reference, so the module takes one small explicit package reference (`System.IdentityModel.Tokens.Jwt` 8.x); it is the same assembly the framework's `JwtBearer` package depends on.
- **ProblemDetails** as the project-standard error shape.
- **`WebApplicationFactory<Program>`** integration testing — real HTTP pipeline, in-memory test configuration, and a custom `ILoggerProvider` that captures log lines for the log-hygiene assertion.

## Security decisions

- **Fail closed at startup**: `IAMConfig.ValidateConfiguration` throws before the host binds when `IAM:DevelopmentLogin` is present outside `Development`, and when it is incomplete in `Development` (verified by three integration tests plus a live run of the built DLL with a `Production` config that contains the section).
- **Generic 401**: wrong email and wrong password produce byte-identical ProblemDetails, so an attacker cannot enumerate valid emails.
- **Fixed-time comparison** for email and password to resist timing side channels.
- **No secret logging**: password and token values are never written to the log pipeline; the login handler logs only the email. An integration test captures all log output during a failed login and asserts the password never appears.
- **CORS locked to the Vite dev origin** and only enabled in `Development`.
- The development signing key lives in `appsettings.Development.json` — a development-only file, documented, never used to issue tokens outside `Development`.

## Alternatives deliberately postponed

- A real identity store (EF Core + PostgreSQL), password hashing and lockout arrive in B004/B005.
- JWT *bearer* authentication middleware (validating the token on protected routes) arrives in B002 with `GET /auth/me`; B001 only issues tokens.
- Refresh tokens, token rotation, registration, and password recovery are later slices.
- Rate limiting, CAPTCHA, and audit logging for login attempts are out of scope for S01.

## How to verify

Prerequisites: .NET 10 SDK. (This machine has no Linux `dotnet` CLI; the Windows SDK is reachable through WSL interop as `dotnet.exe`.)

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
```

Live demo (run from `src/api/TenantForge.Api`):

```bash
# Development API on :5000
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --urls http://0.0.0.0:5000

curl -i http://localhost:5000/health
curl -i -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}'
curl -i -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"wrong"}'
```

Prove fail-closed behavior (built DLL, real Production configuration):

```bash
dotnet.exe build TenantForge.sln
# 1) Production without the section: starts, login is 401
ASPNETCORE_ENVIRONMENT=Production dotnet.exe src/api/TenantForge.Api/bin/Debug/net10.0/TenantForge.Api.dll
# 2) Production WITH an IAM:DevelopmentLogin section (e.g. appsettings.Production.json or
#    env vars IAM__DevelopmentLogin__Email=... IAM__DevelopmentLogin__Password=... IAM__DevelopmentLogin__SigningKey=...):
#    startup fails with "The 'IAM:DevelopmentLogin' section is present but the environment is 'Production'."
```

## Review questions

1. Why does the handler resolve `ICredentialChecker` as a nullable `[FromServices]` parameter instead of a required dependency? What must change in B004, and what deliberately does not?
2. The fail-closed check runs after `builder.Build()` via `IamModule.ValidateIamModuleConfiguration`, not when services are registered in `AddIamModule`. What goes wrong if you move it back (think about when configuration sources are finalized, and about `WebApplicationFactory` test configuration)?
3. Wrong-email and wrong-password responses are byte-identical. What would an attacker be able to learn if the 401 detail leaked which field was wrong, and why is that worse than a generic message even though the endpoint is "just" a dev shortcut?
