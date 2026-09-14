# Architecture direction

This document records the intended destination. Each capability is introduced only when an active task needs it.

For current IAM module behavior — routes, configuration, identifiers, domain model, persistence, authentication/authorization and tests — read the [IAM module handbook](modules/IAM.md) instead of this document; it is the current-state source and is kept in sync by every IAM-touching task.

## System shape

TenantForge is a modular monolith with a separate React single-page application.

```text
Browser
  → React application
  → ASP.NET Core API host
  → IAM module
  → PostgreSQL
```

The first milestone contains one deployable API and one deployable web application. Module boundaries prepare the system for growth without paying the operational cost of microservices.

## Backend organization

Features are organized as vertical slices inside a module. A slice keeps its endpoint, request, validation, handler and response close together.

```text
src/modules/iam/
├── domain/
├── infrastructure/
└── features/
    └── login/
```

Rules:

- Domain concepts belong to the module that owns them.
- The API host composes modules but does not contain IAM business rules.
- Modules do not query another module's tables directly.
- Contracts are introduced only for demonstrated cross-module needs.
- Infrastructure remains replaceable but is not abstracted prematurely.

### BuildingBlocks admission rule

`TenantForge.BuildingBlocks` is the only shared backend project. It is deliberately
not named `Common`, `Shared`, `Utils` or `Helpers`: a semantic name plus an
admission rule keeps it from becoming an ownerless bucket for speculative reuse.

A type enters BuildingBlocks only when it is a stable cross-module contract or an
accepted system-wide primitive, is meaningful without a business module, and has
no dependency on the API host or a module. New code starts in its owning module
and moves only after that rule is met.

Current dependency direction is one-way:

```text
TenantForge.Api
  → TenantForge.Modules.Iam
  → TenantForge.BuildingBlocks
```

BuildingBlocks must not reference the API host, IAM, another module, EF Core,
Npgsql, feature handlers or module-specific options. Its first admitted types are
`TenantForge.BuildingBlocks.Modules.IModuleConfig` and
`TenantForge.BuildingBlocks.Identifiers.TsidId`.

The following stay in IAM until a later visible slice proves a neutral contract:

- `TsidValueConverter` — EF-specific and currently has no second persistence
  consumer;
- `PaginationSupport` — currently couples HTTP query binding, validation text,
  `IQueryable` and EF execution;
- `IAMConfig`, JWT/auth options, seeding, permission keys, authorization,
  entities, DTOs, migrations and feature handlers — IAM-owned behavior.

### Module composition seam

Each module exposes exactly one public static seam class plus a module-config
class; everything else in the module is `internal`, so the API host cannot
reach into feature details. The host composes a module with exactly two calls:
registration before `Build`, asynchronous activation after `Build`.

```csharp
builder.Services.AddIamModule(builder.Environment);

var app = builder.Build();

await app.UseIamModuleAsync();
```

- `IamModule` (public) is the seam, with exactly two public static members:
  - `AddIamModule` — registration only. Adds every IAM-owned service to the
    container. Performs no I/O and makes no pass/fail decision, because
    `WebApplication.CreateBuilder` has not consumed every configuration source
    yet (and `WebApplicationFactory` test configuration is injected later).
  - `UseIamModuleAsync` — activation. Runs once, after `Build`, in
    deterministic order: validate the fully assembled configuration (fail
    closed), install authentication middleware, install authorization
    middleware, apply pending migrations, seed the platform administrator
    idempotently, then map every IAM endpoint.
  - Configuration validation, migration, seeding and endpoint mapping are
    `private` helpers called only from `UseIamModuleAsync`; the host cannot
    call them separately.
- `IModuleConfig` lives in BuildingBlocks as the module-config contract: a
  `SectionName` (the top-level config tag, e.g. `IAM`), `RegisterServices`, and
  `ValidateConfiguration`.
- `IAMConfig : IModuleConfig` reads its section from `IConfiguration`, throws at
  startup when the configuration is not correct (fail closed), and only then
  registers services.
- Activation's database work is genuinely asynchronous (`await`ed), never
  blocked with `.Wait()`/`.Result`/`.GetAwaiter().GetResult()`, and never
  hidden inside a hosted service merely to avoid awaiting it in `Program.cs`.

Future modules (tenancy, audit, ...) follow the same seam shape: one public
module class exposing one registration method and one asynchronous activation
method, one `<X>ModuleConfig : IModuleConfig`, features stay internal.

## Authentication evolution

Authentication intentionally evolves in visible steps:

1. A development-only hardcoded administrator proves the end-to-end login flow.
2. A protected current-user endpoint proves authentication on navigation and refresh.
3. Account persistence replaces the hardcoded credential source without replacing the UI contract.
4. An idempotent seed creates the platform administrator when absent.
5. Session hardening is introduced in a dedicated future task after the basic flows are understood.

The development stub must not be accepted in production configuration.

## Multi-tenancy

The first milestone uses one shared database and explicit `TenantId` ownership for tenant-scoped data.

Tenant context is resolved from authenticated membership and an explicit tenant selection. The API validates that the user is an active member before executing tenant-scoped behavior.

Defense-in-depth expectations:

- tenant-scoped reads require tenant context;
- writes set tenant ownership on the server;
- unique constraints include tenant identity when uniqueness is tenant-local;
- integration tests attempt cross-tenant access;
- missing or invalid tenant context fails closed;
- background work must carry explicit tenant context when it is eventually introduced.

Database row-level security and separate databases are not first-milestone requirements.

## Authorization

Authorization is evaluated on the API using platform role, tenant membership, tenant roles and catalog permissions. UI visibility is not a security boundary.

The first RBAC milestone supports role-based allows. Direct per-user allow/deny overrides and permission caching are postponed until a visible product need justifies them.

## Testing strategy

- Unit tests cover important domain rules with no infrastructure.
- Integration tests execute API behavior against real PostgreSQL through Testcontainers.
- Playwright verifies a small number of critical browser journeys.
- Tenant isolation and unauthorized access are integration-test requirements.

Tests are added with the slice that creates the behavior. The project does not build a large test harness before the first usable flow.

## Local development environment (WSL + Windows .NET SDK)

On the reference machine there is no Linux `dotnet` CLI. The .NET SDK is
reachable through WSL interop as `dotnet.exe` (on PATH, .NET 10; NuGet cache in
`~/.nuget/packages` is shared and network access to nuget.org works). Use
`dotnet.exe build` / `dotnet.exe test` for all backend commands.

Practical consequences:

- `dotnet.exe run` re-applies `Properties/launchSettings.json` (which forces
  `ASPNETCORE_ENVIRONMENT=Development`). For environment-sensitive runs
  (e.g. proving Production fail-closed) run the **built DLL directly**:
  `ASPNETCORE_ENVIRONMENT=Production dotnet.exe src/api/.../bin/Debug/net10.0/TenantForge.Api.dll`.
- A Windows process listening on `localhost` is not reachable from WSL via
  `127.0.0.1`. Bind with `--urls http://0.0.0.0:<port>` and curl through the
  WSL gateway IP (`ip route | head -1` → `default via <gw>`), which is the
  Windows host.
- `WebApplicationFactory` test hosts must be hermetic: point the content root
  at an empty temp directory (`builder.UseContentRoot(...)`) so the real
  `appsettings.*.json` files on disk do not leak into test configuration.

## Observability and operations

Structured logs, health checks and correlation identifiers are introduced when the runnable host exists. Metrics, tracing, background jobs and production deployment are later slices, not bootstrap prerequisites.
