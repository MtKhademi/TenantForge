# S20 — Extract stable cross-module building blocks

## Outcome

TenantForge has one deliberately small `TenantForge.BuildingBlocks` class
library for stable contracts and primitives that every module may use without
depending on IAM. The current API behavior remains unchanged: the same two-call
IAM composition seam works, TSID strings remain the HTTP/JWT representation,
and all existing browser flows continue to work.

The name `BuildingBlocks` is intentional. Do not call the project `Common`,
`Shared`, `Utils` or `Helpers`: those names invite unrelated code to collect
without an owner or dependency rule.

## Dependency direction

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.BuildingBlocks
```

Future modules may reference `TenantForge.BuildingBlocks`. The reverse is
forbidden: BuildingBlocks never references the API host, IAM, another module,
EF Core, Npgsql, feature handlers or module-specific options.

## First admitted building blocks

Only two existing concepts move in this slice:

| Concern | Current owner | New namespace/type |
| --- | --- | --- |
| Module configuration contract | `TenantForge.Modules.Iam.IModuleConfig` | `TenantForge.BuildingBlocks.Modules.IModuleConfig` |
| System-wide TSID boundary helper | `TenantForge.Modules.Iam.Domain.IamId` | `TenantForge.BuildingBlocks.Identifiers.TsidId` |

`IModuleConfig` keeps its existing members and semantics: `SectionName`,
`RegisterServices(IServiceCollection, IHostEnvironment)`, and
`ValidateConfiguration(IHostEnvironment, IConfiguration)`.

`TsidId` keeps the proven B017 rules and method shapes: canonical length,
`NewId`, `IsDefault`, `Format`, `TryParse`, and `TryParseNullable`.
It remains the only place application/module code calls `TsidCreator.GetTsid`
or parses public TSID text.

## Admission rule

A type may enter BuildingBlocks only when all of these are true:

1. it is already used by multiple modules, or an accepted system-wide
   convention requires every future module to use it;
2. its name and behavior make sense without mentioning one business module;
3. it is stable enough that changing it should require reviewing every module;
4. it has no dependency on a feature, module database, module domain model or
   API host implementation; and
5. moving it removes a forbidden module-to-module dependency or prevents one.

BuildingBlocks is not the default location for code that is merely reusable in
theory. New code starts in its owning module and moves only after the rule is
met.

## Deliberately retained in IAM

- `TsidValueConverter` stays in IAM infrastructure. It is an EF Core concern,
  and there is no second persistence consumer yet.
- `PaginationSupport` stays in the IAM feature area. It currently combines
  `HttpRequest` binding, English validation messages, `IQueryable` and EF
  execution; that is not a stable module-neutral contract.
- `IAMConfig`, auth/JWT options, seeding, permission keys, authorization,
  entities, DTOs, migrations and feature handlers remain IAM-owned.
- Health, CORS and application startup remain API-host-owned.

A later task may extract an EF or HTTP-specific building-block project only
after a second real module proves the shared contract. Do not expand B018 while
implementing it.

## Project contract

Create
`src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj`
targeting `net10.0` with nullable reference types and implicit usings enabled.
It may reference `Microsoft.AspNetCore.App` for the existing
`Microsoft.Extensions.*` module-config abstractions and pins
`TSID.Creator.NET` at `1.0.0`.

Move the TSID package reference from IAM to BuildingBlocks; IAM receives it
transitively through its project reference. Add both projects to
`TenantForge.sln`. The API continues to reference IAM only; it must not gain a
direct BuildingBlocks reference just because the project exists.

The only public production types initially exported by the new assembly are
`IModuleConfig` and `TsidId`. Do not add marker interfaces, base entities,
result wrappers, repositories, unit-of-work abstractions, generic module
lifecycles, reflection discovery or dependency scanning.

## Behavior preservation

This is an architecture refactor, not a contract change:

- `builder.Services.AddIamModule(builder.Environment)` remains before
  `Build`;
- `await app.UseIamModuleAsync()` remains after `Build`;
- configuration validation remains late and fail-closed;
- migration and seeding order remains unchanged;
- database IDs remain `bigint`, .NET IDs remain `Tsid`, and API/JWT IDs
  remain canonical strings;
- no route, JSON shape, status code, permission or frontend behavior changes;
- no database migration is created.

## Verification

Automated checks prove:

- the solution builds with BuildingBlocks included;
- IAM has exactly one project reference to BuildingBlocks and no duplicate
  local copies of `IModuleConfig` or `IamId`;
- `IAMConfig` still implements the relocated interface;
- the BuildingBlocks assembly does not reference TenantForge API/IAM, EF Core,
  Npgsql or module-specific assemblies;
- `TsidId` retains all generation, parsing, default-value and canonical-format
  tests from B017;
- IAM's two-method public composition surface remains unchanged;
- the full integration suite passes.

The manual demo signs in, reloads `/api/auth/me`, lists tenants, opens one
tenant-scoped page and verifies that identifiers are still canonical strings.
Restart the API against the same database to prove migration/seeding remains
idempotent. This demo demonstrates no behavior change across the extracted
assembly boundary.

## Acceptance

- The project and namespace names are exactly as specified.
- The dependency graph points only from API to module to BuildingBlocks.
- Exactly `IModuleConfig` and the renamed `TsidId` move; the explicit IAM
  exclusions remain where they are.
- No old `TenantForge.Modules.Iam.IModuleConfig` or
  `TenantForge.Modules.Iam.Domain.IamId` definition/reference remains.
- No endpoint, persistence schema, frontend or runtime behavior changes.
- Architecture and learning documentation explain both the admission rule and
  why `Common` was rejected.
- Focused architecture/identifier tests and the complete backend suite pass.
