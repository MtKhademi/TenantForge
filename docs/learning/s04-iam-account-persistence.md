# B004 — S04 IAM account persistence: learning note

## Files changed and why

- `docker-compose.yml` (new) adds a local PostgreSQL 16 service with a persistent volume and healthcheck. This gives developers a clean, repeatable database for migration practice.
- `src/api/TenantForge.Api/appsettings.Development.json` adds the non-secret local `ConnectionStrings:IamDb` value used by the API and EF tooling in Development.
- `src/api/TenantForge.Api/TenantForge.Api.csproj` adds `Microsoft.EntityFrameworkCore.Design` for EF tooling when the API is the startup project.
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj` adds EF Core design-time support and the Npgsql EF Core provider.
- `src/modules/iam/TenantForge.Modules.Iam/domain/Account.cs` and `AccountStatus.cs` introduce the IAM-owned account model.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/IamDbContext.cs` and `AccountMap.cs` define the EF Core persistence boundary and PostgreSQL mapping for IAM accounts.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/*` is generated EF Core migration code representing the first IAM table in source control.
- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` registers the IAM DbContext through the public module seam and validates the connection string after `builder.Build()`.
- `src/modules/iam/TenantForge.Modules.Iam/Properties/AssemblyInfo.cs` exposes internals only to the integration test assembly, keeping the API host on the public seam.
- `tests/integration/TenantForge.Api.IntegrationTests/ApiFactory.cs` supplies a hermetic test connection string so existing API tests do not depend on real appsettings files.
- `tests/integration/TenantForge.Api.IntegrationTests/IamPersistenceIntegrationTests.cs` proves migrations and constraints against a real PostgreSQL container.
- `tasks/TASKS.md` tracks only the B004 lifecycle state.

## Request flow from endpoint to response

No endpoint changed in this slice.

The existing browser request flow remains:

1. `POST /api/auth/login` still uses the development credential checker and returns the same token/user response.
2. `GET /api/auth/me` still reads the validated JWT claims and returns the same current account shape.
3. `GET /api/platform/dashboard-summary` still requires the platform-admin policy and returns the same dashboard summary shape.

The new persistence flow is startup/tooling/test oriented:

1. The API host calls `builder.Services.AddIamModule(builder.Environment)`.
2. `IAMConfig.RegisterServices` registers `IamDbContext` using `ConnectionStrings:IamDb`.
3. After `builder.Build()`, `ValidateIamModuleConfiguration` checks that the connection string is present.
4. EF tooling or tests call `Database.MigrateAsync()` / `dotnet.exe ef database update`.
5. PostgreSQL receives the `iam_accounts` table, required columns and unique index on `normalized_email`.

## Backend concepts introduced

- **EF Core DbContext ownership:** `IamDbContext` belongs inside the IAM module because IAM owns accounts. The API host composes the module but does not know account table details.
- **Entity mapping:** `AccountMap` turns C# properties into database columns, max lengths, required constraints, enum storage and indexes.
- **Normalized identity:** `NormalizedEmail` stores a canonical uppercase email used for uniqueness. This prevents `admin@example.com` and ` ADMIN@EXAMPLE.COM ` from becoming separate accounts.
- **Migrations as source:** generated migration files are committed so the database shape can be reviewed and replayed from a clean PostgreSQL database.
- **Testcontainers:** persistence tests run against real PostgreSQL instead of an in-memory substitute, so uniqueness and null constraints are enforced by the same database engine used locally.

## Important security decisions

- The model stores `PasswordHash`; it has no `Password` or plaintext password property. Plaintext credential handling remains out of the persistence model.
- Authentication behavior is intentionally unchanged. B004 prepares storage; B005 will replace the development credential checker with database-backed password verification.
- The connection string is validated after host build, so late configuration sources from tests or hosting can participate before fail-fast validation runs.
- The IAM internals stay internal. `InternalsVisibleTo` is limited to the integration test assembly and does not weaken the API host's module boundary.
- No new endpoint exposes account rows, password hashes or database state.

## Alternatives deliberately postponed

- Switching login to the database: postponed to B005 so this slice can focus only on persistence.
- Seeding the platform administrator: postponed to B005, where idempotency and password hashing are the learning goal.
- A repository or unit-of-work abstraction: not needed yet. Direct `DbContext` usage is enough for the next known authentication use case.
- Tenant membership, roles and permissions: later slices own those tables and rules.
- Automatic startup migration: this slice uses explicit `dotnet.exe ef database update` and tests. Startup seeding/migration behavior is handled in the seeded-admin slice.

## Commands and manual steps to verify

```bash
docker compose up -d
dotnet.exe ef database update \
  --project src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj \
  --startup-project src/api/TenantForge.Api/TenantForge.Api.csproj \
  --context IamDbContext

dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests \
  --filter "FullyQualifiedName~IamPersistenceIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
```

Development smoke used in this slice:

```bash
dotnet.exe run --project src/api/TenantForge.Api --urls http://0.0.0.0:5099
GW=<gateway from `ip route`>
curl -i -X POST "http://$GW:5099/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}'
curl -i "http://$GW:5099/api/auth/me" -H "Authorization: Bearer <token>"
curl -i "http://$GW:5099/api/platform/dashboard-summary" -H "Authorization: Bearer <token>"
```

## Review questions

1. Why do we store both `Email` and `NormalizedEmail`, and which one should a login query use in B005?
2. Why are PostgreSQL integration tests better than an in-memory EF provider for proving uniqueness and required-column constraints?
3. What risk would be introduced if the `Account` entity had a public `Password` property next to `PasswordHash`?
