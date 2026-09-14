# B018 — Extract stable cross-module building blocks

**Status:** planned

**Owner:** backend

**Source:** S20

**Depends on:** B017

**Source slice:** [S20](../slices/020-building-blocks.md)

## Goal

Create one small `TenantForge.BuildingBlocks` project and move the two proven
system-wide seams out of IAM: `IModuleConfig` and the TSID helper currently
named `IamId`. Preserve every HTTP, JWT, persistence, startup and browser
behavior.

The learning goal is dependency direction: shared code is defined by stable
ownership and dependency rules, not by the fact that two pieces of code happen
to look reusable.

## Fixed design

Do not spend implementation time choosing names or adding extra abstractions.
Use these exact paths and names:

```text
src/building-blocks/
└── TenantForge.BuildingBlocks/
    ├── TenantForge.BuildingBlocks.csproj
    ├── Modules/
    │   └── IModuleConfig.cs
    └── Identifiers/
        └── TsidId.cs
```

Namespaces and types:

```csharp
TenantForge.BuildingBlocks.Modules.IModuleConfig
TenantForge.BuildingBlocks.Identifiers.TsidId
```

Dependency graph:

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.BuildingBlocks
```

BuildingBlocks must have no `ProjectReference`. IAM references
BuildingBlocks. The API keeps only its IAM project reference and must not
reference BuildingBlocks directly.

## Project file

Create `TenantForge.BuildingBlocks.csproj` as an ordinary
`Microsoft.NET.Sdk` class library with:

- `TargetFramework` = `net10.0`;
- nullable enabled;
- implicit usings enabled;
- `FrameworkReference Include="Microsoft.AspNetCore.App"` so the existing
  module-config signature can use `IConfiguration`, `IServiceCollection`
  and `IHostEnvironment` without creating a second package-version policy;
- exact `PackageReference Include="TSID.Creator.NET" Version="1.0.0"`.

Move the TSID package reference and its explanatory comment out of
`TenantForge.Modules.Iam.csproj`; do not leave a duplicate direct reference.
Add the new project to `TenantForge.sln` with normal Debug/Release build
configuration. Do not alter target frameworks or unrelated package versions.

## Move 1 — module configuration contract

Move
`src/modules/iam/TenantForge.Modules.Iam/IModuleConfig.cs`
to
`src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs`.

Keep the interface public and keep the contract byte-for-byte equivalent:

```csharp
string SectionName { get; }

void RegisterServices(
    IServiceCollection services,
    IHostEnvironment environment);

void ValidateConfiguration(
    IHostEnvironment environment,
    IConfiguration configuration);
```

Only its namespace changes to `TenantForge.BuildingBlocks.Modules`. Update
`IAMConfig` and `IamModule` imports/usages. Do not alter when registration or
validation runs, and do not add methods such as migration, seeding, endpoint
mapping or activation to this interface.

Delete the original IAM file after all references compile. There must be one
definition of `IModuleConfig` in the repository.

## Move 2 — system TSID seam

Move and rename
`src/modules/iam/TenantForge.Modules.Iam/domain/IamId.cs`
to
`src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs`.

Make it `public static class TsidId` because multiple module assemblies are its
intended consumers. Preserve these exact members and B017 behavior:

```csharp
public const int CanonicalLength = 13;
public static Tsid NewId();
public static bool IsDefault(Tsid id);
public static string Format(Tsid id);
public static bool TryParse(string? value, out Tsid id);
public static Tsid? TryParseNullable(string? value);
```

Preserve all guards:

- blank/wrong-length input is false;
- 13-character all-digit input is false so the bigint representation is never
  accepted as public input;
- non-ASCII and invalid Crockford characters are false without throwing;
- all-zero/default TSID is false;
- lower-case valid input is accepted and formatted back to canonical upper-case;
- generation still calls `TsidCreator.GetTsid()`.

Update comments so they describe a system-wide building block, not an IAM-owned
seam or a live B017 task. Rename all production/test calls from `IamId` to
`TsidId` and add the new namespace. Do not add a wrapper class or compatibility
alias; the repository must have no remaining `IamId` symbol.

The domain type remains the package's `Tsid` struct. Database storage remains
PostgreSQL `bigint`; HTTP/JSON/`Location`/JWT values remain canonical strings.

## Exact change inventory

Expected production edits:

- new BuildingBlocks project and its two source files;
- `TenantForge.sln`;
- IAM project file;
- IAM `IAMConfig.cs` and `IamModule.cs`;
- all IAM domain entities calling `IamId.NewId`/`IsDefault`;
- Login, CurrentAccount, TenantDiscovery, Users, Tenants, TenantMembers, Roles,
  Invitations and Audit code calling `IamId.TryParse`/`Format`;
- comments in `TsidValueConverter` that name the old helper.

Expected test edits:

- rename `IamIdTests.cs` and its test class to `TsidIdTests`;
- update all integration-test imports and calls to `TsidId`;
- extend `IamModuleCompositionTests` or add a focused
  `BuildingBlocksArchitectureTests` class.

Expected docs:

- `docs/architecture.md`;
- `docs/learning/b018-building-blocks.md`;
- current backend-agent and vertical-slice guidance;
- active task lifecycle files only at delivery.

Use `rg` before finishing to find every `IamId`, old interface namespace and
direct `TSID.Creator.NET` call. Package namespace imports for the `Tsid`
struct are valid; generation/parsing outside `TsidId` are not.

## Architecture tests

Add focused tests that make the boundary executable:

1. `typeof(IModuleConfig).Assembly.GetName().Name` and
   `typeof(TsidId).Assembly.GetName().Name` both equal
   `TenantForge.BuildingBlocks`.
2. `IModuleConfig.IsAssignableFrom(typeof(IAMConfig))` is true.
3. BuildingBlocks' exported production types are exactly the two intended
   types; compiler-generated members do not count.
4. BuildingBlocks referenced assemblies contain no name beginning with
   `TenantForge.Modules.` or `TenantForge.Api`, and no EF Core/Npgsql
   assembly.
5. IAM references BuildingBlocks and the API does not gain a direct
   BuildingBlocks project reference.
6. Existing reflection assertions still prove `IamModule` exposes exactly
   `AddIamModule` and `UseIamModuleAsync`.

For the project-reference assertions, prefer inspecting the committed project
XML relative to a hermetic test content root or use a small repository
architecture test with an explicit, reviewed path. Do not introduce an
architecture-test framework package for two assertions.

Move the existing B017 identifier tests rather than weakening them. They must
still cover 5,000 generated IDs, long round-trip, canonical length/case, default,
GUID-shaped, decimal, invalid Crockford and non-ASCII inputs.

## Shared-code admission rule

Add this rule to architecture and agent guidance:

A type enters BuildingBlocks only if it is a stable cross-module contract or an
accepted system-wide primitive, is meaningful without a business module, and
does not depend on the API host or a module. Reusability speculation is not
enough. New code starts in its owning module.

Document why the following stay in IAM:

- `TsidValueConverter`: EF-specific, no second persistence consumer;
- `PaginationSupport`: currently couples HTTP query binding, validation text,
  `IQueryable` and EF execution;
- `IAMConfig`, JWT/auth, seeder, permission catalog, entities, DTOs, features
  and migrations: IAM-owned behavior.

Also document why the project is not named `Common`: a semantic name plus an
admission rule prevents it becoming an ownerless utility bucket.

## Behavior and security invariants

This refactor creates no endpoint and changes no accepted contract.

- Keep `AddIamModule` registration-only before `Build`.
- Keep `UseIamModuleAsync` validation → auth middleware → migration → seed →
  endpoint mapping order after `Build`.
- Late test-host configuration remains visible and Production remains
  fail-closed.
- Invalid TSID/JWT/tenant context remains denied without exceptions or data
  disclosure.
- No database migration, table/column/index change or data rewrite.
- No JSON shape, route, status, permission, pagination or frontend change.
- Do not log secrets or identifiers newly.

## Verification commands

Run with the repository's Windows SDK through WSL:

```bash
dotnet.exe restore TenantForge.sln
dotnet.exe build TenantForge.sln --no-restore
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TsidIdTests|FullyQualifiedName~BuildingBlocksArchitectureTests|FullyQualifiedName~IamModuleCompositionSurfaceTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build
rg -n "IamId|namespace TenantForge\.Modules\.Iam;[[:space:]]*public interface IModuleConfig|TsidCreator\.GetTsid|Tsid\.From\(" src tests
git diff --check
```

Classify the final search results:

- `IamId`: zero matches;
- old IAM-owned `IModuleConfig` definition: zero matches;
- `TsidCreator.GetTsid` and public-string `Tsid.From`: only inside
  `TsidId.cs`;
- `Tsid.From(long)` may remain in the IAM EF converter and migration tests
  because that is the persistence boundary.

No frontend build is required because `src/web/**` is forbidden and unchanged.

## Repeatable demo

1. Start PostgreSQL and the API using the existing Development configuration.
2. Sign in through the browser; verify login and `GET /api/auth/me`.
3. Open the tenant list and one tenant's members/roles page.
4. Inspect Network: identifiers are still canonical 13-character strings and
   no backing bigint is exposed.
5. Stop and restart the API against the same database; sign in again and prove
   migration/seeding remains idempotent.
6. Show the solution dependency graph or project files: API → IAM →
   BuildingBlocks, with no reverse reference.

## Acceptance checklist

- [ ] New project path, assembly and namespaces exactly match this Spec.
- [ ] BuildingBlocks contains only `IModuleConfig` and `TsidId` production types.
- [ ] API → IAM → BuildingBlocks is the only TenantForge project direction.
- [ ] Original IAM files/symbols are removed; no compatibility wrappers exist.
- [ ] IAM composition, TSID persistence and public string contracts are unchanged.
- [ ] No migration, frontend or unrelated cleanup is included.
- [ ] Architecture tests, identifier tests and full integration suite pass.
- [ ] Architecture and learning docs explain admission/exclusion decisions.
- [ ] Browser restart/demo proves the refactor is behavior-preserving.

## Delivery discipline

Read S20 and this complete Spec before editing. Present the fixed project/type
contract, expected files and explicit exclusions, then wait for plan approval.
Work only on B018. During final delivery set B018 to `done`, replace its Spec
link with `—`, delete exactly this Spec, preserve S20, validate ledger links
and dependency cycles, and open one PR to `main`. Do not start extracting
pagination, EF helpers or another module.
