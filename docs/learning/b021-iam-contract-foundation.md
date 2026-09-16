# B021 — IAM Contract foundation: pagination, login, account and dashboard types

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`
  (new) — a bare `net10.0` class library with **zero** `PackageReference`,
  `FrameworkReference` and `ProjectReference` entries. It holds IAM's
  HTTP-facing types; it must never need ASP.NET, EF Core, Npgsql or even
  `TenantForge.BuildingBlocks`, because HTTP contracts carry only canonical
  strings and plain values.
- `src/modules/iam/TenantForge.Modules.Iam.Contract/Queries/PaginationQuery.cs`,
  `Responses/PaginationMetadata.cs`, `Requests/LoginRequest.cs`,
  `Responses/LoginResponse.cs`, `Responses/LoginUserResponse.cs`,
  `Responses/CurrentAccountResponse.cs`, `Responses/TenantDiscoveryResponse.cs`,
  `Responses/DiscoveredTenantResponse.cs`, `Responses/DashboardSummaryResponse.cs`
  (new) — the first nine contract types, each now a `public sealed record`
  with identical members (some were `internal sealed`, some `public` but not
  `sealed`; the merge target is `public sealed`). `PaginationQuery.Offset`
  and `PaginationMetadata.From` moved with their records — pure arithmetic,
  no framework dependency.
- `TenantForge.sln` — added the Contract project (same pattern as the
  BuildingBlocks entry).
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj` —
  one new `ProjectReference` to the Contract project, alongside the existing
  BuildingBlocks reference. The API host gains **no** new reference: it still
  only calls `AddIamModule`/`UseIamModuleAsync`.
- `features/pagination/PaginationSupport.cs` — the two record declarations
  were removed; the `TryBind`/`PageAsync`/`Parse` logic (which binds
  `HttpRequest` and executes `IQueryable`/EF) stays module-internal. Added
  `using` for the two new namespaces.
- `features/account/TenantDiscoveryFeature.cs` — removed the
  `TenantDiscoveryResponse`/`DiscoveredTenantResponse` declarations; handler
  logic unchanged.
- Deleted `features/login/LoginRequest.cs`, `features/login/LoginResponse.cs`,
  `features/account/CurrentAccountResponse.cs`,
  `features/dashboard/DashboardSummaryResponse.cs` (each became empty).
- `using`-only additions in `LoginFeature.cs`, `CurrentAccountFeature.cs`,
  `DashboardSummaryFeature.cs`, `UsersFeature.cs`, `TenantsFeature.cs`,
  `TenantMembersFeature.cs`, `RolesFeature.cs`, `InvitationsFeature.cs`,
  `AuditFeature.cs` — the seven feature files that bind pagination or build a
  moved response still import `TenantForge.Modules.Iam.Features.Pagination`
  for `PaginationSupport`; the moved types now resolve via
  `TenantForge.Modules.Iam.Contract.{Requests,Queries,Responses}`.
- `docs/modules/IAM.md` (Section 3 dependency diagram, Section 4 source-code
  map), `docs/architecture.md` (system dependency diagram),
  `docs/building-blocks/README.md` (Section 8 exclusion row now names the
  Contract project's home for the two pagination records).

Excluded, exactly as the slice requires: `AuthenticatedAccount` (a
credential-check result, not serialized to a caller), the
`PaginationSupport` binding/execution logic, and every type in
`users/`, `tenants/`, `tenantmembers/`, `roles/`, `invitations/`, `audit/`
(those move in B022/B023). No `Commands/` or `Enums/` folders were created —
no current IAM type belongs there.

## 2. Request flow from endpoint to response

`POST /api/auth/login` → `LoginFeature.MapLoginFeature` binds the JSON body
into `LoginRequest` (now from `Contract.Requests`) → `ICredentialChecker`
checks the persisted account → `JwtIssuer.Issue` mints the HS256 token → the
handler builds `LoginResponse`/`LoginUserResponse` (now from
`Contract.Responses`) → `Results.Ok`. Nothing in the handler changed; the
type's home assembly changed. `GET /api/auth/me/tenants` shows the same
pattern for queries: `PaginationSupport.TryBind` (module-internal, reads
`HttpRequest`) produces a `Contract.Queries.PaginationQuery`, the EF query
runs, and `PaginationSupport.PageAsync` returns a
`Contract.Responses.PaginationMetadata` alongside the items.

## 3. Backend concepts introduced

- **The `Module → Module.Contract` dependency pattern.** A module's public
  HTTP promise lives in a second, reference-free class library beside the
  implementation. `TenantForge.Modules.Iam` (EF Core, Npgsql, migrations,
  seeding, feature handlers) references the Contract project; the Contract
  project references nothing. A future second module will reference
  `TenantForge.Modules.Iam.Contract` only — never `TenantForge.Modules.Iam` —
  to read an IAM request/response shape, without dragging in the module's
  persistence stack.
- **Reference-free contract project.** Zero `ProjectReference`/
  `PackageReference`/`FrameworkReference` is the point: it forces every
  contract type to be a plain record of strings/numbers/bools/collections.
  If a "contract" type ever needs `IQueryable`, `HttpRequest` or `Tsid`, it
  is the wrong kind of type for that project. (HTTP IDs are canonical 13-char
  TSID *strings*, which is why the Contract project never needs
  `TenantForge.BuildingBlocks`.)
- **Namespace-per-kind convention.** `Requests/` (JSON body), `Queries/`
  (query string), `Responses/` (returned to the caller); `Commands/` and
  `Enums/` exist in the convention but are created only when a real type
  belongs there — no empty folders.
- **Behavior-preservation proof for a pure relocation.** The full existing
  integration suite (112 tests) passing unmodified is the acceptance proof
  that every route, status code and JSON shape is unchanged. No new test is
  needed to prove a move moved nothing; the old assertions do that. The
  locked architecture test for the exported surface arrives with the final
  batch (B023's `IamContractArchitectureTests`).

## 4. Security decisions

- No authorization, authentication or configuration behavior changed.
  Fail-closed rules, the `PlatformAdmin` claim policy, tenant-isolation
  checks and the 401/403 non-disclosure rule are all untouched — proven by
  the suite.
- The visibility change (`internal sealed` → `public sealed`) is a
  deliberate boundary re-drawing, not a security change: the types were
  always serialized to HTTP callers; they were previously reachable from
  outside the assembly only via `InternalsVisibleTo` to the test assembly.
  They now have an explicit, reviewable public surface.
- No secrets, credentials or tokens are logged; nothing new is logged.

## 5. Alternatives deliberately postponed

- **Second-consumer proof.** There is no second module yet; B021 establishes
  the convention with the first, smallest batch of already-delivered types.
  The slice document records why that is not premature abstraction (the types
  are exercised HTTP contracts, not invented shapes).
- **`Commands/` and `Enums/` sub-namespaces.** No current IAM mutation has a
  distinct command object separate from its `*Request`, and no response
  serializes a domain enum directly (every status/role crosses HTTP as a
  plain string) — so neither folder is created yet.
- **The exported-surface architecture test.** Added in B023 once all 32 types
  are in place, mirroring `BuildingBlocksArchitectureTests`.
- **Moving the remaining 23 types.** Split into B022 (users/tenants/tenant
  members) and B023 (roles/invitations/audit) to keep each diff small and
  independently reviewable.
- **A living Contract handbook (`docs/contracts/iam.md`).** Deliberately a
  later task (B024), after the full 32-type roster is delivered — the same
  ordering B020 used after B018.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
# Expected: build succeeded; Passed: 112, Failed: 0, Skipped: 0
```

Grep proof (only usages, no second declaration, inside the module's
`features/` folder):

```bash
grep -rn "PaginationQuery\|PaginationMetadata\|LoginRequest\|LoginResponse\|LoginUserResponse\|CurrentAccountResponse\|TenantDiscoveryResponse\|DiscoveredTenantResponse\|DashboardSummaryResponse" src/modules/iam/TenantForge.Modules.Iam/features/
```

Manual demo (API on `http://0.0.0.0:5000`, curl through the WSL gateway IP):

1. `POST /api/auth/login` with the seeded admin → `200`
   `{accessToken, expiresAtUtc, user{id, email, displayName, isPlatformAdmin}}`.
2. With the token: `GET /api/auth/me` → `200` `{id, email, displayName,
   isPlatformAdmin}`; `GET /api/auth/me/tenants?pageNumber=1&pageSize=5` →
   `200` `{tenants[], pagination{pageNumber, pageSize, totalCount,
   totalPages, hasPreviousPage, hasNextPage}}`; `GET
   /api/platform/dashboard-summary` → `200` `{environment, apiStatus,
   platformAdminCount, generatedAtUtc}`.
3. Stop and restart the API against the same database → startup completes,
   migration/seeding are a no-op (idempotent), login still returns 200.

## 7. Three review questions

1. `PaginationMetadata.From(PaginationQuery, int)` is a *method* on a
   contract record, while `PaginationSupport.TryBind`/`PageAsync` stay in the
   module. What is the precise line between "part of the contract shape" and
   "module infrastructure" — and where would that line move if a future
   module needed to compute `totalPages` itself?
2. The Contract project has zero references, yet `TenantForge.Modules.Iam`
   now depends on it. Why does that direction (implementation → contract)
   let a future module consume IAM shapes without pulling in EF Core/Npgsql,
   and why must the API host *not* reference the Contract project today even
   though it transitively receives the types?
3. Why is "the full existing integration suite passes unmodified" sufficient
   proof that no HTTP contract changed — and what would a test look like that
   *locks* the Contract assembly's exported surface (preview of B023's
   `IamContractArchitectureTests`)?
