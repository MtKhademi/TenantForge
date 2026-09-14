# B018 — Extract stable cross-module building blocks

## 1. Files changed and why

- `src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj` (new) —
  a small, ordinary class library. It carries the `TSID.Creator.NET` package
  reference that used to live directly in IAM, because the TSID helper is the
  thing that actually needs it now.
- `src/building-blocks/TenantForge.BuildingBlocks/Modules/IModuleConfig.cs` (new,
  moved from IAM) — the module-configuration contract (`SectionName`,
  `RegisterServices`, `ValidateConfiguration`), unchanged in shape, moved out of
  IAM's namespace because it is meant for every future module, not just IAM.
- `src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs` (new,
  moved and renamed from `IamId`) — the same B017 TSID generation/parsing/
  formatting rules, now `public` instead of `internal`, because a second module
  will eventually need the identical rules without depending on IAM.
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj` —
  removed the direct `TSID.Creator.NET` package reference (IAM now gets `Tsid`
  transitively through BuildingBlocks) and added one `ProjectReference` to
  BuildingBlocks.
- `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`,
  `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — now implement/import
  `TenantForge.BuildingBlocks.Modules.IModuleConfig` instead of a local
  interface.
- IAM domain entities and feature handlers — every call site that used to say
  `IamId.NewId()`, `IamId.IsDefault(...)`, `IamId.TryParse(...)`,
  `IamId.TryParseNullable(...)` or `IamId.Format(...)` now calls the same
  methods on `TenantForge.BuildingBlocks.Identifiers.TsidId`.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TsidValueConverter.cs` —
  only its comment referencing the old helper name was updated; the converter
  itself stays IAM-owned (see below).
- `tests/integration/TenantForge.Api.IntegrationTests/TsidIdTests.cs` (renamed
  from `IamIdTests.cs`) — same 20 tests, same coverage (5,000 generated IDs,
  long round-trip, canonical length/case, default, GUID-shaped, decimal,
  invalid Crockford, non-ASCII), now exercising `TsidId`.
- `tests/integration/TenantForge.Api.IntegrationTests/BuildingBlocksArchitectureTests.cs`
  (new) — makes the dependency-direction rule executable instead of just
  documented (see below).
- Every other integration test file that referenced `IamId` was updated to call
  `TsidId` instead; no test behavior changed.
- `docs/architecture.md`, `.opencode/agents/backend-mentor.md`,
  `.opencode/skills/vertical-slice-delivery/SKILL.md` — documented the
  admission rule, the exact dependency direction, and the explicit exclusions.

## 2. Request flow from endpoint to response

Nothing about a live request changed in this slice. The host still composes
IAM through exactly two calls:

```csharp
builder.Services.AddIamModule(builder.Environment);   // before Build
var app = builder.Build();
await app.UseIamModuleAsync();                         // after Build
```

What changed is only *where the pieces `AddIamModule`/`UseIamModuleAsync`
depend on are defined*:

- `IAMConfig : IModuleConfig` now points at
  `TenantForge.BuildingBlocks.Modules.IModuleConfig` instead of an
  IAM-local interface with the same shape.
- Every endpoint handler that parses a route/body/JWT identifier or formats one
  for a response now calls `TsidId.TryParse`/`TsidId.Format` from
  BuildingBlocks instead of the old `IamId` in IAM's `domain` folder.

A request to, say, `POST /api/tenants/{tenantId}/roles` still resolves the
same way: ASP.NET Core routes it to the same IAM feature handler, which still
parses `tenantId` into a `Tsid`, still queries EF with that `Tsid`, and still
emits a canonical TSID string back. The only difference is which assembly the
parsing/formatting code lives in.

## 3. Backend concepts introduced

**Dependency direction as a design decision, not a byproduct.** Before this
slice, `IModuleConfig` and the TSID helper were "in IAM" purely because IAM
was the first and only module. That accident of history is exactly the trap
this slice avoids: as soon as a second module is imagined, both types are
obviously not IAM's business rules — they are contracts every module needs.
Moving them into a project that IAM depends on (and that depends on nothing
module-specific) makes the one-way dependency (`Api → Modules → BuildingBlocks`)
enforceable rather than just conventional.

**An explicit admission rule beats "this looks reusable."** Almost anything
*could* be called reusable. The rule this slice documents — stable
cross-module contract or accepted system-wide primitive, meaningful without a
business module, no dependency on the API host or a module — is what decided
that `IModuleConfig` and the TSID helper qualify today while `TsidValueConverter`
(EF-specific, no second persistence consumer) and `PaginationSupport` (still
tangled with HTTP binding, English validation text and `IQueryable`) do not.
New code still starts in its owning module by default.

**Making an architecture rule testable.** `BuildingBlocksArchitectureTests`
asserts, by reflection and by reading the actual `.csproj` XML, that:

- `IModuleConfig` and `TsidId` live in the `TenantForge.BuildingBlocks`
  assembly;
- `IAMConfig` really implements the relocated interface;
- BuildingBlocks exports exactly those two production types;
- BuildingBlocks references no `TenantForge.Modules.*`/`TenantForge.Api`/EF
  Core/Npgsql assembly;
- IAM has a `ProjectReference` to BuildingBlocks and the API does not.

This converts "please don't let the API reference BuildingBlocks directly" from
a code-review reminder into something CI actively enforces.

## 4. Important security decisions

- **No behavior changed at any HTTP/JWT/persistence boundary.** This is a pure
  move-and-rename: database IDs are still `bigint`, .NET code still uses
  `Tsid`, and every response/JWT subject still carries only the canonical
  13-character string. The security-relevant parsing rules from B017 (reject
  GUID-shaped input, reject 13-digit decimal input, reject non-ASCII/invalid
  Crockford without throwing, reject the all-zero sentinel) moved verbatim into
  `TsidId` and are re-proven by the renamed `TsidIdTests`.
- **`IModuleConfig`'s fail-closed contract is unchanged.** `IAMConfig` still
  throws from `ValidateConfiguration` when configuration is missing or unsafe,
  and `UseIamModuleAsync` still calls validation before authentication
  middleware, migration or seeding. Only the interface's namespace moved.
- **The dependency-direction rule is itself a security boundary.** Keeping
  BuildingBlocks free of EF Core/Npgsql/module references means a future
  module cannot accidentally reach into another module's persistence layer
  just because "the shared project happened to reference it."

## 5. Alternatives deliberately postponed

- **Extracting `TsidValueConverter` into BuildingBlocks** — rejected in the
  Spec because it is an EF Core concern with no second persistence consumer
  yet; moving it now would be reusability speculation, not a proven need.
- **Extracting `PaginationSupport`** — rejected because it currently mixes
  HTTP query binding, English validation messages, `IQueryable` and EF
  execution; it is not yet a module-neutral contract.
- **A generic module-lifecycle/discovery framework** — there is still exactly
  one module (IAM); building reflection-based module discovery now would be
  speculative generality with no second consumer to validate the design
  against.
- **Naming the project `Common`, `Shared`, `Utils` or `Helpers`** — explicitly
  rejected. Those names invite unrelated code to accumulate without an owner
  or a rule; `BuildingBlocks` plus the documented admission rule keeps the
  project's scope deliberate.

## 6. Commands and manual steps to verify the slice

```bash
dotnet.exe restore TenantForge.sln
dotnet.exe build TenantForge.sln --no-restore
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TsidIdTests|FullyQualifiedName~BuildingBlocksArchitectureTests|FullyQualifiedName~IamModuleCompositionSurfaceTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build
```

Expected results from this slice:

- solution build: `0 Error(s)`;
- focused filter: `26` passing tests;
- full integration suite: `112` passing tests.

Classification search (grep, since `rg` is unavailable in this shell):

- `IamId`: zero matches in `src`/`tests`;
- old IAM-owned `IModuleConfig` definition: zero matches;
- `TsidCreator.GetTsid` and public `Tsid.From(string)`: only inside `TsidId.cs`;
- `Tsid.From(long)`: only in `TsidValueConverter.cs` (persistence boundary) and
  `TsidIdTests.cs` (long round-trip assertion) — both expected.

Manual browser/API demo outline:

1. Start PostgreSQL and the API using the existing Development configuration.
2. Sign in through the browser; confirm login and `GET /api/auth/me` still
   work exactly as before.
3. Open the tenant list and one tenant's members/roles page; identifiers in
   the Network tab remain canonical 13-character strings.
4. Stop and restart the API against the same database; sign in again to prove
   migration/seeding activation is still idempotent.
5. Inspect the project files (or `dotnet.exe list ... reference`) to see
   `TenantForge.Api → TenantForge.Modules.Iam → TenantForge.BuildingBlocks`
   with no reverse or skipped reference.

## 7. Three review questions

1. `TsidValueConverter` and `PaginationSupport` both "look reusable" in the
   sense that other modules could eventually want similar code. Why do they
   stay in IAM instead of moving into BuildingBlocks in this same slice?
2. `BuildingBlocksArchitectureTests` reads the committed `.csproj` XML instead
   of only using reflection. Why is reflection alone (checking loaded
   assembly references) not enough to prove "the API project does not
   reference BuildingBlocks"?
3. `IModuleConfig`'s validate-then-register contract did not change in this
   slice — only its namespace did. What would have gone wrong here if this
   slice had *also* tried to redesign when validation runs while doing the
   move?
