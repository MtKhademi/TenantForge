# B005 — S05 seeded platform administrator: learning note

## Files changed and why

### New

- `src/modules/iam/TenantForge.Modules.Iam/features/login/AuthOptions.cs` — binds `IAM:Auth` (only the JWT signing key). Replaces the old `DevelopmentLoginOptions`, which mixed credentials and the signing key in one section.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/SeedAdminOptions.cs` — binds `IAM:SeedAdmin:{Email,Password,DisplayName}`, the one-time bootstrap identity. Exposes `IsConfigured` so seeding can be safely skipped when absent.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/JwtConstants.cs` — issuer/audience/token lifetime as plain code constants (see "Alternatives deliberately postponed").
- `src/modules/iam/TenantForge.Modules.Iam/features/login/JwtIssuer.cs` — issues tokens for an `AuthenticatedAccount`. Exposes `CanIssue` so the endpoint can fail closed instead of throwing when no signing key is configured.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/JwtBearerSigningKeyOptions.cs` — assigns the JwtBearer validation key from `AuthOptions` at runtime (renamed from `DevelopmentJwtBearerOptions`; same fail-closed random-key behavior when absent).
- `src/modules/iam/TenantForge.Modules.Iam/features/login/AuthenticatedAccount.cs` — the verified identity (`Id`, `Email`, `DisplayName`, `IsPlatformAdmin`) a successful login produces. The only source the login endpoint may take claims from.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/ICredentialChecker.cs` — new async contract: `Task<AuthenticatedAccount?> AuthenticateAsync(email, password)`. Replaces the old synchronous `bool Check(...)`.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/AccountCredentialChecker.cs` — the real implementation: looks up the account by normalized email, verifies the password hash, requires `Active` status. A missing account, wrong password and disabled account are all indistinguishable — every one returns `null`.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/PlatformAdminSeeder.cs` — idempotent, concurrency-safe seeding (see "Backend concepts introduced").

### Modified

- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` — registers `AuthOptions`/`SeedAdminOptions` (bound lazily), `IPasswordHasher<Account>`, and the real `AccountCredentialChecker`/`PlatformAdminSeeder` in **every** environment as **scoped** services (see "Important security decisions" for the DI-lifetime bug this caught). Validation now enforces that `IAM:SeedAdmin` is either fully present or fully absent, and refuses an unsafe (too short) seed password outside Development.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — adds the public seam method `SeedIamModuleAsync(IServiceProvider)`: applies pending migrations, then seeds idempotently inside a DI scope. Logs only the configured email, never the password or hash.
- `src/modules/iam/TenantForge.Modules.Iam/features/login/LoginFeature.cs` — now `async`; resolves `ICredentialChecker`/`JwtIssuer` as required services (not nullable — they exist in every environment now); checks `issuer.CanIssue` before attempting to authenticate.
- `src/modules/iam/TenantForge.Modules.Iam/features/dashboard/DashboardSummaryFeature.cs` — `platformAdminCount` is now `db.Accounts.CountAsync(a => a.IsPlatformAdmin && a.Status == AccountStatus.Active)` instead of the constant `1`.
- `src/api/TenantForge.Api/Program.cs` — one added line: `await IamModule.SeedIamModuleAsync(app.Services);`, placed after `UseAuthorization()` and before `MapHealth()`.
- `src/api/TenantForge.Api/appsettings.Development.json` — `IAM:DevelopmentLogin` replaced by `IAM:Auth:SigningKey` and `IAM:SeedAdmin:{Email,Password,DisplayName}`.
- `tests/integration/TenantForge.Api.IntegrationTests/ApiFactory.cs` — rewritten to take a shared `IamDbFixture` (real, migrated Testcontainers PostgreSQL) instead of an unreachable connection string; `DevelopmentLoginMode` renamed `IamSeedMode` with a new `NoSigningKey` case.
- `tests/integration/TenantForge.Api.IntegrationTests/{Login,CurrentAccount,Dashboard,ProductionFailClosed}IntegrationTests.cs` — updated to assert the seeded account's real database GUID `id` instead of the old `"development-admin"` constant, and to use the shared fixture/collection.

### Deleted

- `DevelopmentAdminIdentity.cs`, `DevelopmentCredentialChecker.cs`, `DevelopmentJwtIssuer.cs`, `DevelopmentLoginOptions.cs`, `DevelopmentJwtBearerOptions.cs`, old `CredentialChecker.cs` — the entire hardcoded S01 credential path. Nothing in `src/` references these names anymore (verified).

### Test-only new files

- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs` — one shared, migrated PostgreSQL container (via `ICollectionFixture`) reused across the API-level test classes, instead of one container per test class.
- `tests/integration/TenantForge.Api.IntegrationTests/PlatformAdminSeederTests.cs` — direct tests of `PlatformAdminSeeder` (first seed, repeated seed, concurrent race) plus a disabled-account login test, each against its own isolated database so they don't interfere with the shared fixture's seeded row.

## Request flow (endpoint to response)

### Startup (new)

1. `builder.Services.AddIamModule(...)` registers everything (unchanged shape).
2. `app.Build()`, then `ValidateIamModuleConfiguration` — now also validates the seed section is all-or-nothing and (outside Development) not unsafe.
3. **New:** `await IamModule.SeedIamModuleAsync(app.Services)`:
   - `db.Database.MigrateAsync()` — applies any pending migration.
   - If `SeedAdminOptions.IsConfigured` is false, logs and returns (no admin to seed).
   - Otherwise calls `PlatformAdminSeeder.SeedAsync(...)` and logs the outcome (email + created/already-present, never the password/hash).

### `POST /api/auth/login`

1. Same 400 for missing email/password (unchanged).
2. **New:** if `!issuer.CanIssue` (no signing key configured), answer the generic 401 immediately — never attempt to mint a token that could never validate.
3. **New:** `await checker.AuthenticateAsync(email, password)`:
   - Normalize the email, look up the account.
   - If missing, or `Status != Active`, or the hash doesn't verify → `null`.
   - On success, return an `AuthenticatedAccount` built from the persisted row.
4. `null` → the same generic 401 as before (unchanged wording, unchanged shape).
5. Success → `issuer.Issue(account)` builds the JWT from the **persisted** account's id/email/displayName/isPlatformAdmin (previously: hardcoded constants).
6. Response shape is byte-for-byte identical to S01/B004: `accessToken`, `expiresAtUtc`, `user{id,email,displayName,isPlatformAdmin}`.

### `GET /api/auth/me`, `GET /api/platform/dashboard-summary`

Unchanged authorization/claim logic. Only the *source* of `platformAdminCount` changed (constant → `COUNT` query); the response shape is identical.

## Backend concepts introduced

- **Idempotent seeding.** "Seed if absent" is implemented as *lookup, then insert*, which by itself is not concurrency-safe (two processes can both see "absent" and both insert). The unique index on `normalized_email` (from B004) is the actual safety net: a losing insert throws `DbUpdateException`, which the seeder catches and treats as "someone else already seeded it" — not an error. This is the standard "optimistic insert, let the database break the tie" pattern, verified directly with a concurrent-race test (`ConcurrentSeed_RacingProcesses_StillCreateExactlyOneAdministrator`).
- **`IPasswordHasher<TUser>`.** ASP.NET Core's `PasswordHasher<TUser>` (shipped in the shared framework — no extra package needed) produces a salted, iterated PBKDF2 hash string that encodes its own algorithm/iteration/salt, and `VerifyHashedPassword` re-derives and compares it. The `TUser` type parameter isn't read by the default implementation; it exists so custom implementations *could* branch on user-specific data.
- **DI service lifetimes must match their dependencies.** `AccountCredentialChecker` and `PlatformAdminSeeder` both depend on `IamDbContext`, which EF Core registers `Scoped`. Registering them as `Singleton` compiles fine but fails at *runtime* validation (`Cannot consume scoped service ... from singleton`) — the DI container refuses to build the app rather than risk a long-lived object holding a short-lived `DbContext` (which would eventually throw `ObjectDisposedException` or leak connections). This was caught by the integration test suite, not the build.
- **Replacing an implementation behind a stable interface.** `ICredentialChecker` changed shape (`bool Check` → `Task<AuthenticatedAccount?> AuthenticateAsync`) because the *caller* (`LoginFeature`) needed the verified identity, not just a boolean — but the **HTTP contract** the browser sees never moved.

## Important security decisions

- **Indistinguishable failure.** Unknown email, wrong password, and disabled account all produce the same `null` from `AccountCredentialChecker`, which `LoginFeature` turns into the same generic 401 `"Invalid credentials"` — with no difference in timing-sensitive code paths beyond what `IPasswordHasher` already does internally. Verified by `DisabledAccount_LoginReturns401_IndistinguishableFromWrongPassword`.
- **Fail closed on missing signing key.** `JwtIssuer.CanIssue` is checked *before* calling the credential checker, so a misconfigured host (no `IAM:Auth:SigningKey`) never even attempts password verification — it simply cannot authenticate anyone. Verified by `NoSigningKeyConfigured_LoginFailsClosedWith401_EvenWithValidSeededAccount`.
- **Unsafe production seed configuration is rejected at startup.** Outside Development, a `SeedAdmin` password shorter than 12 characters fails `ValidateConfiguration` with an exception that names the *configuration path*, never the value. Verified by `UnsafeShortSeedPasswordOutsideDevelopment_FailsStartup`, which also asserts the literal password text does not leak into the exception message.
- **Partial configuration is always rejected.** `IAM:SeedAdmin` must be all three fields or none — a partially-filled section (e.g., email+password but no display name) throws in *any* environment, not just Production, so a broken deployment fails loudly rather than silently skipping seeding or seeding a malformed account.
- **No secrets in logs.** The only log line from seeding is `Platform administrator seeding {created|already-present} for {Email}.` — the email is an identity, not a secret; the password and hash never appear. The existing `PasswordValueNeverAppearsInLogOutput` test still passes.
- **The stored password is a hash, verified structurally.** `FirstSeed_CreatesExactlyOnePlatformAdministrator` asserts `account.PasswordHash != Options.Password` — the value on disk is provably not the plaintext.

## Alternatives deliberately postponed

- **Making `JwtConstants` (issuer/audience/lifetime) configurable.** Considered and explicitly declined: these are facts about *this application's identity*, not secrets or per-deployment tuning, and making them configurable would add validation surface (empty audience? zero lifetime?) for a need that doesn't exist yet. Only the signing key — the actual secret — lives in configuration.
- **Admin creation UI, multiple platform roles, registration/password recovery, refresh-token rotation, tenants and tenant roles** — all explicitly out of scope per the S05 slice; none were touched.
- **A generic repository/unit-of-work layer** — `PlatformAdminSeeder` and `AccountCredentialChecker` use `IamDbContext` directly, same as B004's guidance: no abstraction beyond what the current known use case needs.

## How to verify

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
```

Clean-database demo (this machine has no Linux `dotnet`; commands run through `dotnet.exe`, the Windows SDK via WSL interop):

```bash
docker compose up -d
# Simulate a clean database (docker compose down -v is restricted in this environment):
docker exec tenantforge-postgres psql -U tenantforge -d tenantforge \
  -c 'DROP TABLE IF EXISTS iam_accounts CASCADE; DROP TABLE IF EXISTS "__EFMigrationsHistory" CASCADE;'

dotnet.exe run --project ./src/api/TenantForge.Api/ --urls "http://0.0.0.0:5100"
# Log shows: Applying migration '...InitialIamPersistence' then
#            Platform administrator seeding created for admin@tenantforge.local.

# Ctrl+C, then run again (repeat as many times as you like):
dotnet.exe run --project ./src/api/TenantForge.Api/ --urls "http://0.0.0.0:5100"
# Log shows: Platform administrator seeding already-present for admin@tenantforge.local.

docker exec tenantforge-postgres psql -U tenantforge -d tenantforge -c "SELECT COUNT(*) FROM iam_accounts;"
# count = 1, regardless of how many times the API was started.

GW=<gateway from `ip route`>
curl -i -X POST "http://$GW:5100/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}'
curl -i "http://$GW:5100/api/auth/me" -H "Authorization: Bearer <token>"
curl -i "http://$GW:5100/api/platform/dashboard-summary" -H "Authorization: Bearer <token>"
# platformAdminCount: 1, a real query result.
```

To prove the old hardcoded checker is gone:

```bash
grep -rln "DevelopmentCredentialChecker\|DevelopmentAdminIdentity\|DevelopmentJwtIssuer\|DevelopmentLoginOptions" src/modules src/api
# no output — none of these names exist in source anymore.
```

## Review questions

1. `PlatformAdminSeeder.SeedAsync` checks "does this email already exist?" and *then* inserts — that check-then-act is not concurrency-safe by itself. What actually makes it safe, and which test proves it?
2. `AccountCredentialChecker.AuthenticateAsync` returns `null` for three different situations (unknown email, wrong password, disabled account). Why is collapsing all three into one outcome a deliberate security decision rather than a missed opportunity for a more helpful error message?
3. `AccountCredentialChecker` and `PlatformAdminSeeder` are registered `Scoped`, while `JwtIssuer` is `Singleton`. What is the rule that decides this, and what would go wrong (and when would you find out) if `AccountCredentialChecker` were registered `Singleton` instead?
