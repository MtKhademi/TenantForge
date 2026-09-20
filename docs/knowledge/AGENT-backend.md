# Backend agent knowledge

Operational memory for the agent running `/backend-task`. Read this file
completely before implementing any backend task. It describes the current
codebase, not its history. Keep it short — link to the detailed handbook
instead of copying it here.

Human-facing explanations live in [HUMAN-backend.md](HUMAN-backend.md) and
`docs/learning/`. Do **not** read those to implement a task.

## Fast facts

| Fact | Value |
| --- | --- |
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| Shape | Modular monolith, one visible vertical slice per task |
| Database | PostgreSQL 16 (Docker Compose), EF Core + Npgsql |
| Solution | `TenantForge.sln` |
| Modules today | `iam`, `shop` |
| Test project | `tests/integration/TenantForge.Api.IntegrationTests` (xUnit + Testcontainers) |
| CLI on this machine | `dotnet.exe`, **not** `dotnet` |

## Layout

```text
src/api/TenantForge.Api/                     # host: Program.cs, HealthEndpoints.cs, appsettings
src/building-blocks/TenantForge.BuildingBlocks/
  Identifiers/TsidId.cs                      # TSID formatting/parsing
  Modules/IModuleConfig.cs                   # module registration + validation contract
  Permissions/                               # IPermissionCatalogContributor, AggregatedPermissionCatalog, PermissionGroup, PermissionDescriptor
src/modules/iam/TenantForge.Modules.Iam/          # domain/ features/ infrastructure/ + IamModule.cs, IAMConfig.cs, AuthorizationPolicyNames.cs
src/modules/iam/TenantForge.Modules.Iam.Contract/ # Requests/ Queries/ Responses/ — IAM's public HTTP shapes, zero outgoing references
src/modules/shop/TenantForge.Modules.Shop/        # domain/ features/ infrastructure/ + ShopModule.cs, ShopConfig.cs
tests/integration/TenantForge.Api.IntegrationTests/
```

Shop has **no** `.Contract` project. Its DTOs live beside the feature in
`features/<area>/<Area>Contracts.cs`.

## Architecture boundaries

- Dependency direction is **API → module → BuildingBlocks**. BuildingBlocks
  never references the API or a module. Modules never reference each other's
  implementation project.
- `TenantForge.Modules.Iam` references `TenantForge.Modules.Iam.Contract`.
  Nothing else does.
- BuildingBlocks is not a `Common`/`Utils` bucket. A type is admitted only
  with proven cross-module or accepted system-wide evidence — see the
  admission checklist in `docs/building-blocks/README.md`.
- Cross-module communication today goes through BuildingBlocks contracts
  (`IPermissionCatalogContributor`) or raw SQL against the other module's
  tables when a task explicitly allows it (Shop reads IAM membership this
  way in `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`). Do not add a
  project reference between modules.

## Module composition seam

Every module exposes exactly two public methods, called from
`src/api/TenantForge.Api/Program.cs` and nowhere else:

```csharp
builder.Services.AddShopModule(builder.Environment);   // registration only, no I/O
await app.UseShopModuleAsync();                        // activation
```

Activation owns, in this order: configuration validation (fail closed) →
pending migrations → endpoint mapping. IAM additionally owns authentication
middleware, authorization middleware and idempotent platform-admin seeding.
Never validate at registration time, never expose those steps as separate host
calls, never add a hosted service to hide the await.

## Feature pattern

One folder per capability under `features/`:

- `<Area>Feature.cs` — a `MapXFeature(this IEndpointRouteBuilder)` extension
  plus its handlers. Types stay `internal`.
- `<Area>Contracts.cs` — request/response records (Shop), or the matching
  record in `TenantForge.Modules.Iam.Contract` (IAM).
- The module's `MapXModule` calls every `MapXFeature()` in a fixed order.

Copy the nearest existing feature in the same module before inventing a shape.

## Identifiers

- Database identity and foreign keys are PostgreSQL `bigint`.
- Domain and EF model values are `Tsid`.
- HTTP request/response IDs and JWT subjects are **canonical 13-character TSID
  strings**.
- Never serialize the backing integer to JSON and never accept a decimal ID
  from a client. Conversion lives in `TsidValueConverter.cs` /
  `ShopTsidValueConverter.cs`.

## API contract rules

- The backend contract is the source of truth. The frontend never invents a
  different permanent shape.
- Errors use RFC 7807 Problem Details as the single JSON error body
  (`Results.Problem` / `ValidationProblem`). Do not add a second error shape.
- List endpoints return the shared pagination envelope. `PaginationMetadata`
  has **six** members: `pageNumber`, `pageSize`, `totalCount`, `totalPages`,
  `hasPreviousPage`, `hasNextPage`.
- Shop HTTP shapes are documented in `docs/design/shop/http-contracts.md`;
  IAM's in `docs/contracts/iam.md` and `docs/modules/IAM.md`.
- A slice normally adds no more than one or two endpoints, and only with a
  named current or immediately dependent frontend consumer.
- Do not change an accepted contract without updating the active task first.

## Authentication and authorization

- Tenant isolation and permission checks are **server-side**. Hiding UI is not
  authorization. Default to deny when tenant or permission context is missing.
- IAM registers the `PlatformAdmin` policy
  (`AuthorizationPolicyNames.PlatformAdmin`): unauthenticated → 401,
  authenticated but failing the `isPlatformAdmin` claim → 403.
- Tenant-scoped Shop routes check membership and permission through
  `ShopAuthorization`. Delivered permission keys today:
  `Shop.Catalog.Manage`, `Shop.Shipping.Manage`. The tenant Owner role
  bypasses the permission check.
- New permission keys are registered by the module's
  `IPermissionCatalogContributor` (`IamPermissionCatalogContributor`,
  `ShopPermissionCatalogContributor`) and aggregated in `Program.cs`. The
  frontend reads the catalog from the server; keep the two lists in step.
- Development-only shortcuts must fail closed outside `Development`.
- Never log passwords, tokens or secrets.

## Persistence and migrations

- One `DbContext` per module: `IamDbContext`, `ShopDbContext`, each under
  `infrastructure/`.
- Every entity has a sibling `<Entity>Map.cs` `IEntityTypeConfiguration`. Put
  column names, lengths, indexes and conversions there, not in the entity.
- Migrations live in `infrastructure/Migrations/` and are applied at startup by
  the module's activation phase.
- Use `TimeProvider` as the injectable clock, never `DateTime.UtcNow` inline.
- Handle races with a row lock plus a database constraint, not a bare
  `AnyAsync` check.

## Testing

- One integration test project covers everything:
  `tests/integration/TenantForge.Api.IntegrationTests`.
- `ApiFactory` / `IamDbFixture` boot the real host against a Testcontainers
  PostgreSQL instance. **Docker must be running.**
- Every security-sensitive behavior needs both the happy path and the
  relevant unauthorized/forbidden path.
- `BuildingBlocksArchitectureTests` and `IamContractArchitectureTests` lock the
  dependency direction and the exported type roster by hand. A surface change
  means editing those hand-written lists too.
- Never weaken a test or narrow a filter to obtain a pass.

## Commands

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test                                     # whole integration suite (needs Docker)
dotnet.exe test --filter FullyQualifiedName~ShopOrderIntegrationTests
docker compose up -d postgres
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

EF migration (run from the repository root):

```bash
dotnet.exe ef migrations add <Name> \
  --project src/modules/shop/TenantForge.Modules.Shop/TenantForge.Modules.Shop.csproj \
  --startup-project src/api/TenantForge.Api/TenantForge.Api.csproj \
  --context ShopDbContext
```

## Known traps

1. There is no Linux `dotnet` binary on this machine. Every build/test/run
   command is `dotnet.exe`.
2. `dotnet.exe run` forces `Development` through `launchSettings.json`. To
   prove Production fail-closed behavior, run the built DLL directly with
   `ASPNETCORE_ENVIRONMENT=Production`.
3. A Windows process is not reachable from WSL on `127.0.0.1`. Bind
   `--urls http://0.0.0.0:<port>` and curl the WSL gateway IP from
   `ip route | head -1`.
4. `WebApplicationFactory` hosts must be hermetic: point the content root at an
   empty temp directory so real `appsettings.*.json` files cannot leak into
   test configuration.
5. `dotnet.exe test` fails with a container error, not a compile error, when
   Docker is not running. Start Docker before blaming the code.
6. Shop entities are not all tenant-scoped. `ShopPaymentAttempt` has no
   `TenantId` column — filter through `ShopOrder`.
7. Adding a permission key in the backend without adding it to the frontend
   catalog (or the reverse) silently breaks the permission matrix.

## Decisions future tasks must preserve

- Two-phase module composition (`AddXModule` / `UseXModuleAsync`), no third
  public entry point.
- `bigint` storage / `Tsid` domain / 13-char string wire for every identifier.
- RFC 7807 as the only error body.
- BuildingBlocks admission rule; `Module.Contract` holds only delivered HTTP
  shapes with no ASP.NET/EF/Npgsql dependency.
- Server-side authorization, fail closed by default.
- No speculative endpoint, abstraction, background service or empty project.

## Read-first gates (conditional, from `AGENTS.md`)

Read the whole handbook during discovery **only** when the task touches that
area, then classify the diff against its change-impact checklist before review:

| Task touches | Read first | Declaration if nothing changed |
| --- | --- | --- |
| `src/modules/iam/**` or an IAM contract in BuildingBlocks/the host | `docs/modules/IAM.md` | `IAM.md impact: none — <specific reason>` |
| `src/building-blocks/**`, a reference to it, or `IModuleConfig`/`TsidId` | `docs/building-blocks/README.md` | `BuildingBlocks docs impact: none — <specific reason>` |
| `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a reference to it | `docs/contracts/iam.md` | `IAM Contract docs impact: none — <specific reason>` |

A vague "docs not needed" does not satisfy these gates.

## More detail

- `AGENTS.md` — shared rules, ownership, ledger lifecycle, Git safety
- `docs/architecture.md` — system shape, testing strategy, WSL environment
- `docs/design/shop/` — Shop HTTP contracts, inventory, payments
- `.opencode/skills/vertical-slice-delivery/SKILL.md` — delivery protocol
- `.opencode/skills/module-contract-project/SKILL.md` — when a module earns a
  `.Contract` project

## Maintaining this file

Update it in the same task that changes a fact above. Add durable facts only:
no debugging notes, no speculation, no abandoned approaches, no secrets, no
large code blocks. If a task changed nothing here, say so in the completion
report instead of padding the file.
