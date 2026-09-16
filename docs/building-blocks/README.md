# BuildingBlocks handbook

**Read this first for BuildingBlocks questions.** This document describes
current, merged `TenantForge.BuildingBlocks` behavior only. It is not a task
diary: it contains no `planned`/task-status language and no historical
narrative. When it disagrees with code or tests, the code is the source of
truth — report the mismatch and verify before trusting either.

`TenantForge.BuildingBlocks` exists to hold the small number of stable
contracts that genuinely cross module boundaries, and nothing else. Read
[Section 1](#1-purpose-and-strict-non-purpose) before proposing any addition.

## Table of contents

1. [Purpose and strict non-purpose](#1-purpose-and-strict-non-purpose)
2. [Fast facts](#2-fast-facts)
3. [Dependency rule](#3-dependency-rule)
4. [Exported-type catalog](#4-exported-type-catalog)
5. [`IModuleConfig` contract](#5-imoduleconfig-contract)
6. [`TsidId` contract](#6-tsidid-contract)
7. [Admission checklist](#7-admission-checklist)
8. [Explicit exclusions](#8-explicit-exclusions)
9. [Change and compatibility policy](#9-change-and-compatibility-policy)
10. [Test and verification map](#10-test-and-verification-map)
11. [Decision records](#11-decision-records)
12. [Change-impact checklist](#12-change-impact-checklist)

## 1. Purpose and strict non-purpose

`TenantForge.BuildingBlocks` contains only:

- stable contracts used across module boundaries (today: the module
  registration/activation contract every module implements); or
- explicitly accepted system-wide primitives (today: the public identifier
  seam every HTTP/JWT boundary uses).

It is **not**:

- a `Common`, `Shared`, `Utils` or `Helpers` bucket;
- a home for code that is merely reusable in theory ("might be reusable
  later" is not evidence — see [Section 7](#7-admission-checklist));
- a business-domain module;
- a shortcut for one module to expose internals to another;
- a place for generic repositories, unit of work, base entities, result
  wrappers, service locators or reflection-based module discovery.

New code always starts in its owning module. Extraction into BuildingBlocks
requires completed admission evidence ([Section 7](#7-admission-checklist)),
not a preference for central placement.

## 2. Fast facts

| Fact | Value |
| --- | --- |
| Project directory / assembly | `src/building-blocks/TenantForge.BuildingBlocks/` (`TenantForge.BuildingBlocks.csproj`, assembly `TenantForge.BuildingBlocks`) |
| Target framework | `net10.0` |
| Allowed project-reference direction | A module may reference BuildingBlocks; BuildingBlocks references no TenantForge project — see [Section 3](#3-dependency-rule) |
| Direct framework/package dependencies | `FrameworkReference Microsoft.AspNetCore.App`; `PackageReference TSID.Creator.NET` (pinned `1.0.0`) |
| Exported public production type count | 2 — verified by `BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes` |
| Current consuming projects | `TenantForge.Modules.Iam` only (via one `ProjectReference`) |
| Architecture test location | `tests/integration/TenantForge.Api.IntegrationTests/BuildingBlocksArchitectureTests.cs` |
| Handbook update declarations | `BuildingBlocks docs impact: updated — <sections/types>` / `BuildingBlocks docs impact: none — <specific reason>` — see [Section 12](#12-change-impact-checklist) |

## 3. Dependency rule

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.BuildingBlocks
```

Verified against the real project files
(`ProjectReferences_FlowFromApiToIamToBuildingBlocksOnly` re-verifies this on
every test run):

- `src/api/TenantForge.Api/TenantForge.Api.csproj` references
  `TenantForge.Modules.Iam.csproj`, **not** BuildingBlocks directly;
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj`
  references `TenantForge.BuildingBlocks.csproj`;
- `src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj`
  has **zero** `ProjectReference` entries.

Rules:

- the API host may depend on modules;
- a module may depend on BuildingBlocks;
- BuildingBlocks must have no `ProjectReference` to the API host or any
  module — `BuildingBlocksArchitectureTests.BuildingBlocks_DoesNotReferenceApiModulesEfOrNpgsql`
  also asserts the compiled assembly references no `TenantForge.Modules.*`,
  `TenantForge.Api`, `Microsoft.EntityFrameworkCore.*` or `Npgsql.*` assembly;
- one business module must not reference another module to obtain a shared
  primitive — with a single module (IAM) today, this rule has no live
  counter-example yet, but it governs any second module;
- a direct API → BuildingBlocks reference requires a real host-owned consumer
  and explicit architecture review; it is not added for convenience. No such
  reference exists today.

Allowed external dependencies, read directly from
`TenantForge.BuildingBlocks.csproj`:

- `FrameworkReference Microsoft.AspNetCore.App` (shared framework, not a
  NuGet package);
- `PackageReference TSID.Creator.NET Version="1.0.0"` — pinned exactly
  because it is the system-wide public identifier boundary; do not float it
  independently of a deliberate, tested upgrade.

Any new package reference on this project is a public architectural cost: it
is inherited by every consuming module and requires a handbook update (fast
facts + dependency rule + [Section 9](#9-change-and-compatibility-policy)).

## 4. Exported-type catalog

One row per public production type exported by the compiled assembly
(non-compiler-generated types only, matching
`BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes`):

| Type | Namespace/path | Purpose | Current consumers | Dependencies | Contract tests | Change risk |
| --- | --- | --- | --- | --- | --- | --- |
| `IModuleConfig` | `TenantForge.BuildingBlocks.Modules.IModuleConfig` (`src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs`) | Module registration/validation contract composed by the API host | `IAMConfig : IModuleConfig` (`src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`), consumed through `IamModule.AddIamModule`/`UseIamModuleAsync` | `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting` (framework abstractions only) | `BuildingBlocksArchitectureTests.MovedContracts_LiveInBuildingBlocksAssembly`, `IamModuleCompositionSurfaceTests.*` | High — every module implementation and the API composition call depend on the exact three members |
| `TsidId` | `TenantForge.BuildingBlocks.Identifiers.TsidId` (`src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs`) | System-wide public identifier seam: generate/format/parse the canonical 13-character TSID string | Every IAM entity/feature that generates or parses a public identifier (`src/modules/iam/TenantForge.Modules.Iam/**`); every integration test that asserts identifier shape | `TSID.Creator.NET` (`Tsid`, `TsidCreator`) | `TsidIdTests.cs` (13 tests), `BuildingBlocksArchitectureTests.MovedContracts_LiveInBuildingBlocksAssembly` | High — HTTP/JWT identifier shape and PostgreSQL `bigint` round-trip both depend on this exact format/parse behavior |

No other public production type is exported. If delivered code ever adds a
third exported type without updating this table, that is a documentation
gap, not evidence the type is undocumented-but-fine.

## 5. `IModuleConfig` contract

Exact delivered members
(`src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs`):

```csharp
public interface IModuleConfig
{
    string SectionName { get; }

    void RegisterServices(IServiceCollection services, IHostEnvironment environment);

    void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration);
}
```

- **`SectionName`** — the module's top-level configuration key (IAM's
  implementation returns `"IAM"`).
- **`RegisterServices`** — called by the module's public `Add<Module>Module`
  extension method, before `WebApplication.Build()`. Adds every module-owned
  service to the container. Must perform no I/O and make no pass/fail
  decision, because configuration is not fully assembled yet at this point
  (the host's and `WebApplicationFactory` test host's configuration sources
  alike).
- **`ValidateConfiguration`** — called by the module's public
  `Use<Module>ModuleAsync` extension method, after `Build()`, so every
  configuration source (including test overrides) is visible. Fails closed:
  an incomplete or unsafe configuration throws before the host serves
  traffic.

Ownership and lifecycle rules:

- implementations remain module-owned (`IAMConfig` lives in
  `TenantForge.Modules.Iam`, not in BuildingBlocks);
- registration happens before `Build`; validation happens after `Build`
  through the module's activation seam, so late configuration sources
  (including `WebApplicationFactory` test configuration) remain visible to
  validation;
- migration, seeding, endpoint mapping and authentication/authorization
  middleware installation do **not** belong on this interface — they are
  module-composition-seam concerns (see `IamModule.UseIamModuleAsync`), not
  part of the shared contract;
- adding or changing a member is a breaking cross-module review item: every
  current `IModuleConfig` implementation (today, only `IAMConfig`) and the
  API composition call must be re-verified.

Exact references:

- interface: `src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs`;
- IAM implementation: `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`;
- IAM module seam that calls it:
  `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs`
  (`AddIamModule` calls `RegisterServices`; `UseIamModuleAsync` calls
  `ValidateConfiguration` first, then installs authentication/authorization
  middleware, migrates, seeds, and maps endpoints);
- API composition call: `src/api/TenantForge.Api/Program.cs`
  (`builder.Services.AddIamModule(builder.Environment)` before `Build`,
  `await app.UseIamModuleAsync()` after `Build`);
- composition tests:
  `tests/integration/TenantForge.Api.IntegrationTests/IamModuleCompositionTests.cs`
  (`IamModuleCompositionSurfaceTests` — exactly two public static methods
  with the expected signatures; former per-step host calls are no longer
  public. `IamModuleActivationIntegrationTests` — fresh startup
  migrates/seeds before first login succeeds, and a repeat startup against
  the same database does not duplicate the seeded administrator).

## 6. `TsidId` contract

Exact delivered public API
(`src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs`):

| Member | Signature | Behavior |
| --- | --- | --- |
| `CanonicalLength` | `public const int CanonicalLength = 13;` | Length of the canonical Crockford-base32 string form |
| `NewId` | `public static Tsid NewId()` | Generates a new, globally-unique, time-sortable identifier |
| `IsDefault` | `public static bool IsDefault(Tsid id)` | True when `id` is the struct's all-zero "unset" value |
| `Format` | `public static string Format(Tsid id)` | Formats as the canonical, always upper-case 13-character string |
| `TryParse` | `public static bool TryParse(string? value, out Tsid id)` | Parses without throwing; rejects malformed input (see below) |
| `TryParseNullable` | `public static Tsid? TryParseNullable(string? value)` | Returns `null` (not the struct default) for missing/invalid input; used for optional route/query parameters |

Three representations, without moving persistence ownership out of each
module's infrastructure:

| Boundary | Representation |
| --- | --- |
| PostgreSQL module table | `bigint` |
| .NET module/domain | `Tsid` |
| HTTP/JWT | canonical 13-character string |

`TryParse` rejects (each has a dedicated `TsidIdTests` case):

- `null`, empty or whitespace-only input;
- wrong length (rejects GUID-shaped input — both hyphenated 36-character and
  bare 32-character forms — and most decimal input in one length check);
- a 13-character all-digit string (a valid Crockford string at the byte
  level, but rejected explicitly so a client can never send or learn to
  depend on the raw `bigint` backing value);
- any character above ASCII code point 127 (the underlying package indexes
  its alphabet table by raw code point; this bound is applied before the
  package call so a non-ASCII character can never escape as an
  `IndexOutOfRangeException`);
- non-Crockford characters (`I`, `L`, `O`, `U` are not in the alphabet);
- the all-zero decoded value (the domain's "unset" sentinel — a syntactically
  valid Crockford string that decodes to `0` is still rejected as a public
  identifier).

Input is accepted case-insensitively (`Tsid.From` normalizes case), but
`Format` always re-emits the canonical upper-case form — so an accepted
lower-case value round-trips to canonical upper-case, not back to lower-case.

Generation, parsing and formatting is one shared seam; EF Core value
conversion and entity-property mapping remain owned by each module's
infrastructure — see [Section 8](#8-explicit-exclusions).

Tests: `tests/integration/TenantForge.Api.IntegrationTests/TsidIdTests.cs`
(generation/uniqueness/time-sortability, `long` round-trip, canonical
formatting, case normalization, and every rejection case above).

Current consumers: every IAM entity and feature that generates or parses a
public identifier, and the login/JWT issuer for the `sub` claim
(`src/modules/iam/TenantForge.Modules.Iam/**`).

Operational rule (no environment value recorded here): a deployment running
more than one writer process must assign each process a unique
`TSIDCREATOR_NODE` value so generated identifiers do not collide across
processes.

## 7. Admission checklist

For every proposed public type or addition, complete this table before
extraction. Missing any answer means: reject extraction, keep the type in
its owning module.

| Evidence | Required answer |
| --- | --- |
| Problem | What dependency/duplication exists now? |
| Consumers | Two real module consumers, or which accepted system-wide rule? |
| Ownership | Why is no business module the correct owner? |
| Minimal API | Smallest public contract and why each member is needed |
| Dependencies | New packages/frameworks and why unavoidable |
| Compatibility | Source/binary/data/transport impact on consumers |
| Security | Boundary, validation, secret or authorization implications |
| Tests | Architecture/contract behavior locked by tests |
| Alternatives | Why keeping it module-local is insufficient |

"Cleaner", "reusable", "future modules may need it" and "best practice" are
**not** sufficient evidence for any row. With exactly one business module
(IAM) currently in the system, the "two real module consumers" bar for a
brand-new business-type extraction cannot yet be met; only an accepted
system-wide primitive (like `TsidId`) or the module contract itself
(`IModuleConfig`) can satisfy admission today.

## 8. Explicit exclusions

Post-B018 exclusions and their current owners — each stays in IAM for a
concrete reason, not a promise of future extraction:

| Excluded concern | Current owner | Why it stays out |
| --- | --- | --- |
| `TsidValueConverter` (EF `ValueConverter<Tsid, long>`) | `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TsidValueConverter.cs` (`internal`) | EF-Core-specific persistence mapping; has no second persistence consumer, and BuildingBlocks must not reference `Microsoft.EntityFrameworkCore` (enforced by `BuildingBlocksArchitectureTests.BuildingBlocks_DoesNotReferenceApiModulesEfOrNpgsql`) |
| Pagination (`PaginationSupport`, `PaginationQuery`, `PaginationMetadata`) | `PaginationSupport` in `src/modules/iam/TenantForge.Modules.Iam/features/pagination/` (`internal`); the `PaginationQuery`/`PaginationMetadata` records in `src/modules/iam/TenantForge.Modules.Iam.Contract/` (`public sealed`) | Couples HTTP query binding, validation text, `IQueryable` and EF execution — not a boundary-neutral contract. B021/S23 relocated the two plain records to the IAM Contract project (a module-owned public HTTP surface, not a cross-module primitive); `PaginationSupport`'s ASP.NET/EF logic stays IAM-internal |
| `IAMConfig` and its options (`AuthOptions`, `SeedAdminOptions`, `JwtBearerSigningKeyOptions`) | `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs` and `features/login/` | Module-specific configuration implementation and options; only `IModuleConfig` itself is the shared contract |
| Authentication/JWT (`JwtIssuer`, `JwtConstants`, `AccountCredentialChecker`) | `src/modules/iam/TenantForge.Modules.Iam/features/login/` | IAM-owned business/security behavior, not a cross-module primitive |
| Seeding (`PlatformAdminSeeder`) | `src/modules/iam/TenantForge.Modules.Iam/features/login/` | IAM-specific startup behavior |
| Permission catalog and tenant authorization (`RolesFeature`, `AuthorizationPolicyNames`) | `src/modules/iam/TenantForge.Modules.Iam/features/roles/`, `src/modules/iam/TenantForge.Modules.Iam/AuthorizationPolicyNames.cs` | IAM business/domain rules, not infrastructure-neutral |
| Entities, DTOs, features and migrations | `src/modules/iam/TenantForge.Modules.Iam/domain/`, `features/**`, `infrastructure/Migrations/` | Module business/domain and schema history — never a BuildingBlocks concern |
| API health/CORS/startup host behavior | `src/api/TenantForge.Api/Program.cs`, `src/api/TenantForge.Api/HealthEndpoints.cs` | Host-composition behavior, not a module or cross-module contract |

## 9. Change and compatibility policy

Classify every BuildingBlocks change into exactly one of these classes, and
follow the required review for that class:

| Class | Example | Required review |
| --- | --- | --- |
| Implementation-only/non-breaking | Internal refactor of `TsidId.TryParse` that preserves every input/output pair | No public-surface documentation change required; still record `BuildingBlocks docs impact: none — <reason>` |
| Additive public API | A new `TsidId` helper member | Update [Section 4](#4-exported-type-catalog) and the relevant contract section ([Section 5](#5-imoduleconfig-contract)/[Section 6](#6-tsidid-contract)); enumerate every current consumer that could adopt it |
| Signature/semantic breaking change | Changing `IModuleConfig.ValidateConfiguration`'s parameters, or changing which inputs `TsidId.TryParse` accepts | Enumerate and re-verify every current consumer before implementation; update the contract section and [Section 11](#11-decision-records) if it reverses a documented decision |
| Package/framework dependency change | Upgrading or replacing `TSID.Creator.NET` | Update [Section 2](#2-fast-facts) and [Section 3](#3-dependency-rule); a persisted-identifier-format change also requires `docs/modules/IAM.md` review |
| Project-reference/dependency-direction change | Adding any `ProjectReference` to BuildingBlocks, or a second module referencing BuildingBlocks | Update [Section 3](#3-dependency-rule) and [Section 2](#2-fast-facts) consumers row; re-run `ProjectReferences_FlowFromApiToIamToBuildingBlocksOnly` |
| Identifier/serialization/storage change | Any change to the canonical string shape, `bigint` mapping, or default-value semantics of `TsidId` | Update [Section 6](#6-tsidid-contract) and every owning module's handbook that persists or transports the identifier — for IAM, that is `docs/modules/IAM.md` |

A BuildingBlocks public API change must enumerate every current consumer
(today: `TenantForge.Modules.Iam` and its test suite) before implementation,
not after. Transport or persistence changes require their owning module's
docs review too; for example, a `TsidId` semantic change also triggers
`docs/modules/IAM.md`'s change-impact checklist.

No semantic-versioning or package-publication system is introduced here;
this is an internal project-reference library consumed only through
in-repo `ProjectReference`.

## 10. Test and verification map

| Contract | Test class/file |
| --- | --- |
| Assembly/type ownership (both types live in the BuildingBlocks assembly; `IAMConfig` implements `IModuleConfig`) | `BuildingBlocksArchitectureTests.MovedContracts_LiveInBuildingBlocksAssembly` |
| Exact exported surface (only the two approved types) | `BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes` |
| Allowed/rejected project references (no EF/Npgsql/API/module assembly reference; API→IAM→BuildingBlocks direction only) | `BuildingBlocksArchitectureTests.BuildingBlocks_DoesNotReferenceApiModulesEfOrNpgsql`, `BuildingBlocksArchitectureTests.ProjectReferences_FlowFromApiToIamToBuildingBlocksOnly` |
| `IModuleConfig` implementation/composition (exactly two public static `IamModule` methods with the expected signatures; former per-step calls are no longer public; fresh/repeat startup migrate-and-seed behavior) | `IamModuleCompositionSurfaceTests.IamModule_ExposesExactlyTwoPublicStaticMethods_WithTheExpectedSignatures`, `IamModuleCompositionSurfaceTests.FormerlySeparateHostCalls_AreNoLongerPublic`, `IamModuleActivationIntegrationTests.FreshStartup_MigratesAndSeeds_BeforeFirstLoginSucceeds`, `IamModuleActivationIntegrationTests.RepeatStartup_AgainstTheSameDatabase_DoesNotDuplicateTheAdministrator` |
| `TsidId` generation/parsing/formatting (generation, uniqueness, `long` round-trip, canonical formatting, case normalization, and every rejection case) | `TsidIdTests.cs` (13 `Fact`/`Theory` methods) |
| Integration behavior proving extraction did not alter IAM | Full `TenantForge.Api.IntegrationTests` suite — every IAM HTTP contract exercised in earlier slices still passes unchanged |

Commands:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Focused filters:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~BuildingBlocksArchitectureTests|FullyQualifiedName~TsidIdTests|FullyQualifiedName~IamModuleCompositionSurfaceTests|FullyQualifiedName~IamModuleActivationIntegrationTests"
```

## 11. Decision records

Compact current-state decisions, not a chronological changelog. A superseded
decision is replaced here, not appended alongside the old one.

| Decision | Reason | Revisit only when |
| --- | --- | --- |
| Name the project `BuildingBlocks`, not `Common` | The name plus an admission rule (Section 1/7) keeps it from becoming an ownerless dumping ground | Never for convenience |
| One small project today, two admitted types | Only two stable shared concepts exist: the module contract and the identifier seam | A third type completes the admission checklist with real evidence |
| `TsidValueConverter` remains module-owned | Persistence-specific (EF `ValueConverter`), one consumer (IAM), and BuildingBlocks must not reference EF Core | A second real module needs the identical `Tsid`↔`bigint` mapping and the admission checklist is completed |
| Pagination remains module-owned | Couples HTTP query binding, validation text, `IQueryable` and EF execution — not boundary-neutral | A stable, infrastructure-neutral pagination contract has multiple real consumers |

## 12. Change-impact checklist

| Changed area | Required handbook action |
| --- | --- |
| public type/member/semantics | Update [Section 4](#4-exported-type-catalog) + the owning contract section ([5](#5-imoduleconfig-contract)/[6](#6-tsidid-contract)) + [Section 9](#9-change-and-compatibility-policy) |
| package/framework reference | Update [Section 2](#2-fast-facts) + [Section 3](#3-dependency-rule) + [Section 9](#9-change-and-compatibility-policy) |
| project consumer/reference | Update [Section 3](#3-dependency-rule) (dependency graph) + [Section 2](#2-fast-facts) (consumers) |
| TSID validation/format | Update [Section 6](#6-tsidid-contract) + [Section 10](#10-test-and-verification-map); also check `docs/modules/IAM.md` impact |
| module-config signature/lifecycle | Update [Section 5](#5-imoduleconfig-contract) + [Section 10](#10-test-and-verification-map); also check the affected module's own handbook (`docs/modules/IAM.md` for IAM) |
| exclusion becomes admitted | Update [Section 8](#8-explicit-exclusions) (remove the exclusion) + [Section 4](#4-exported-type-catalog) (add the type) + record completed [Section 7](#7-admission-checklist) evidence |
| test path/command | Update [Section 10](#10-test-and-verification-map) |
| no documented fact changed | Record the exact justified no-impact declaration below |

Use exactly one of these two declarations in self-review and the PR body for
every task that touches `src/building-blocks/**`, a consumer's project
reference to it, or a public BuildingBlocks contract:

```text
BuildingBlocks docs impact: updated — <sections/types>
BuildingBlocks docs impact: none — <specific reason>
```

A vague "docs not needed" does not satisfy this gate. Cross-check a shared
TSID or module-config change against `docs/modules/IAM.md` as well, since IAM
is the current consumer of both contracts.
