# S23 — Separate IAM's public contract from its implementation

## Outcome

TenantForge has a new, deliberately small `TenantForge.Modules.Iam.Contract`
class library that holds every HTTP-facing request, query and response type
IAM currently exposes. `TenantForge.Modules.Iam` (the implementation project:
domain, persistence, feature handlers, endpoint mapping) references
`TenantForge.Modules.Iam.Contract` and keeps 100% of its current behavior.
No route, JSON shape, status code, persisted schema or frontend behavior
changes anywhere in this slice — this is an architecture refactor, not a
contract change, exactly like B018/S20.

The reason to do this now, with IAM still the only module, is written down
here explicitly because it looks like the kind of speculative abstraction
`AGENTS.md` and `docs/building-blocks/README.md` both warn against ("no
speculative backend", "prefer direct, readable code over abstractions
created for hypothetical future requirements", BuildingBlocks' "two real
consumers" admission bar). It is not that: every type moved in this slice is
already a delivered, exercised HTTP contract type — this slice relocates
existing code, it does not invent new shape. The convention it establishes
(`Module.Contract` beside `Module`) is what lets a second module, whenever
one is built, depend on IAM's response/request shapes (for example, to read
a tenant ID from a response type) without pulling in IAM's EF Core,
Npgsql, migrations, seeding and feature-handler internals. `TenantForge.BuildingBlocks`
solves a different problem (shared cross-module primitives); this project
solves "what does IAM promise the outside world" vs. "how IAM implements
that promise."

## Dependency direction

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.Modules.Iam.Contract
        -> TenantForge.BuildingBlocks
```

`TenantForge.Modules.Iam.Contract` has **zero** `ProjectReference` entries
and no `PackageReference`/`FrameworkReference` beyond the bare SDK — every
type it holds is a plain C# record with primitive/string/collection members
(HTTP contracts are always canonical strings, never `Tsid`, so the Contract
project never needs to know about `TenantForge.BuildingBlocks`). The API
host does not reference the Contract project in this slice — it has no
reason to, since it only calls `AddIamModule`/`UseIamModuleAsync`. A future
module adds a `ProjectReference` to `TenantForge.Modules.Iam.Contract`
only — never to `TenantForge.Modules.Iam` itself — the day it genuinely
needs to know an IAM request/response shape.

## Project and naming convention

Create `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`,
targeting `net10.0` with `Nullable` and `ImplicitUsings` enabled, no other
`PropertyGroup`/`ItemGroup` entries. Root namespace
`TenantForge.Modules.Iam.Contract`, with one sub-namespace per kind of
contract type:

| Sub-namespace | Folder | Holds |
| --- | --- | --- |
| `TenantForge.Modules.Iam.Contract.Requests` | `Requests/` | Types bound from a JSON request body |
| `TenantForge.Modules.Iam.Contract.Queries` | `Queries/` | Types bound from query-string parameters |
| `TenantForge.Modules.Iam.Contract.Responses` | `Responses/` | Types returned to the HTTP caller |
| `TenantForge.Modules.Iam.Contract.Commands` | `Commands/` | Reserved — see below |
| `TenantForge.Modules.Iam.Contract.Enums` | `Enums/` | Reserved — see below |

`Commands/` and `Enums/` are **not created empty** in this slice.
`AGENTS.md`'s "do not create empty projects or folders for future modules"
rule applies to sub-folders too. No current IAM request is modeled as a
distinct command object separate from its `*Request` type, and no current
response serializes a domain enum directly (every status/role value crosses
HTTP as a plain string). Create either folder only in a future task that
adds a real type that belongs there, using the admission rule below.

Every moved type becomes `public sealed record` (uniform — some, like
`LoginResponse`, are already `public` but not `sealed`; some, like
`InvitationResponse`, are `internal sealed`; the merge target for all of
them is `public sealed`). Keep every member name, order and type exactly as
today — this is a namespace/project move, not a reshape.

## Admission rule for this project

A type belongs in `TenantForge.Modules.Iam.Contract` when both are true:

1. it is already part of a real, delivered IAM HTTP request or response
   shape (present in `docs/modules/IAM.md` Section 11's endpoint catalog,
   directly or as a nested type of a response listed there); and
2. it carries no ASP.NET (`HttpRequest`, `IEndpointRouteBuilder`), EF Core
   (`IQueryable`, `DbContext`) or Npgsql dependency, and no reference to an
   `internal` domain/infrastructure type.

Everything else stays in `TenantForge.Modules.Iam`. In particular, stay
module-owned:

| Type/concern | File | Why it stays |
| --- | --- | --- |
| `AuthenticatedAccount` | `features/login/AuthenticatedAccount.cs` | The credential-check result the login feature mints a token from — not itself serialized to an HTTP caller; `LoginUserResponse` is the response shape |
| `PaginationSupport` (`TryBind`, `PageAsync`, `Parse`) | `features/pagination/PaginationSupport.cs` | `HttpRequest`-binding and `IQueryable`/EF execution — infrastructure, not a contract shape (exactly the same reasoning B018 used to keep this file IAM-owned) |
| `TenantAccess`, `ActorSnapshot`, `AssignmentValidation`, `RemovedAssignment` | `features/roles/RolesFeature.cs` | Intermediate handler-only computation records; never returned as-is to an HTTP caller |
| Every domain entity and enum (`Account`, `Tenant`, `AccountStatus`, `TenantStatus`, `TenantMembershipRole`, …) | `domain/` | Persistence/domain model; every current response already re-expresses status/role as a plain string, so no domain enum is a contract type today |

## What moves (32 types, grouped for three tasks)

**B021 — foundation (pagination, login, account, dashboard):**

| Type | Current file | New file |
| --- | --- | --- |
| `PaginationQuery` | `features/pagination/PaginationSupport.cs` | `Queries/PaginationQuery.cs` |
| `PaginationMetadata` | `features/pagination/PaginationSupport.cs` | `Responses/PaginationMetadata.cs` |
| `LoginRequest` | `features/login/LoginRequest.cs` | `Requests/LoginRequest.cs` |
| `LoginResponse` | `features/login/LoginResponse.cs` | `Responses/LoginResponse.cs` |
| `LoginUserResponse` | `features/login/LoginResponse.cs` | `Responses/LoginUserResponse.cs` |
| `CurrentAccountResponse` | `features/account/CurrentAccountResponse.cs` | `Responses/CurrentAccountResponse.cs` |
| `TenantDiscoveryResponse` | `features/account/TenantDiscoveryFeature.cs` | `Responses/TenantDiscoveryResponse.cs` |
| `DiscoveredTenantResponse` | `features/account/TenantDiscoveryFeature.cs` | `Responses/DiscoveredTenantResponse.cs` |
| `DashboardSummaryResponse` | `features/dashboard/DashboardSummaryResponse.cs` | `Responses/DashboardSummaryResponse.cs` |

**B022 — depends on B021 (users, tenants, tenant members):**

| Type | Current file | New file |
| --- | --- | --- |
| `CreateUserRequest` | `features/users/UsersFeature.cs` | `Requests/CreateUserRequest.cs` |
| `UsersListResponse` | `features/users/UsersFeature.cs` | `Responses/UsersListResponse.cs` |
| `UserResponse` | `features/users/UsersFeature.cs` | `Responses/UserResponse.cs` |
| `CreateTenantRequest` | `features/tenants/TenantsFeature.cs` | `Requests/CreateTenantRequest.cs` |
| `TenantListResponse` | `features/tenants/TenantsFeature.cs` | `Responses/TenantListResponse.cs` |
| `TenantSummaryResponse` | `features/tenants/TenantsFeature.cs` | `Responses/TenantSummaryResponse.cs` |
| `TenantMembersResponse` | `features/tenantmembers/TenantMembersFeature.cs` | `Responses/TenantMembersResponse.cs` |
| `TenantContextResponse` | `features/tenantmembers/TenantMembersFeature.cs` | `Responses/TenantContextResponse.cs` |
| `TenantMemberResponse` | `features/tenantmembers/TenantMembersFeature.cs` | `Responses/TenantMemberResponse.cs` |

**B023 — depends on B022 (roles, invitations, audit; adds the architecture test):**

| Type | Current file | New file |
| --- | --- | --- |
| `CreateRoleRequest` | `features/roles/RolesFeature.cs` | `Requests/CreateRoleRequest.cs` |
| `UpdateRoleRequest` | `features/roles/RolesFeature.cs` | `Requests/UpdateRoleRequest.cs` |
| `PermissionCatalogResponse` | `features/roles/RolesFeature.cs` | `Responses/PermissionCatalogResponse.cs` |
| `PermissionGroupResponse` | `features/roles/RolesFeature.cs` | `Responses/PermissionGroupResponse.cs` |
| `PermissionResponse` | `features/roles/RolesFeature.cs` | `Responses/PermissionResponse.cs` |
| `TenantRolesResponse` | `features/roles/RolesFeature.cs` | `Responses/TenantRolesResponse.cs` |
| `PagedTenantRolesResponse` | `features/roles/RolesFeature.cs` | `Responses/PagedTenantRolesResponse.cs` |
| `TenantRoleResponse` | `features/roles/RolesFeature.cs` | `Responses/TenantRoleResponse.cs` |
| `ResolvedPermissionsResponse` | `features/roles/RolesFeature.cs` | `Responses/ResolvedPermissionsResponse.cs` |
| `CreateInvitationRequest` | `features/invitations/InvitationsFeature.cs` | `Requests/CreateInvitationRequest.cs` |
| `InvitationListResponse` | `features/invitations/InvitationsFeature.cs` | `Responses/InvitationListResponse.cs` |
| `InvitationResponse` | `features/invitations/InvitationsFeature.cs` | `Responses/InvitationResponse.cs` |
| `AuditListResponse` | `features/audit/AuditFeature.cs` | `Responses/AuditListResponse.cs` |
| `AuditEventResponse` | `features/audit/AuditFeature.cs` | `Responses/AuditEventResponse.cs` |

Each task moves only its own table's rows. Do not move a type ahead of its
assigned task, and do not leave a duplicate definition in both projects —
the old file's type is deleted, not kept as a forwarding alias.

## Mechanical move recipe (apply per type)

1. Create the new file under `TenantForge.Modules.Iam.Contract` at the path
   in the table, with `namespace TenantForge.Modules.Iam.Contract.<Kind>;`
   and the type marked `public sealed record` with the exact same members.
2. Delete the type from its old file. If the old file becomes empty (for
   example `features/login/LoginRequest.cs`), delete the file too. If other
   types remain in the old file (for example `RolesFeature.cs`, which keeps
   its handler logic and the four internal-only records above), remove only
   the moved type's declaration.
3. In every file that referenced the old type, add
   `using TenantForge.Modules.Iam.Contract.<Kind>;` and remove any
   now-unused `using` for the old feature namespace.
4. Add one `<ProjectReference>` from
   `TenantForge.Modules.Iam.csproj` to
   `TenantForge.Modules.Iam.Contract.csproj`, and add the new project to
   `TenantForge.sln`, once, in B021 (B022/B023 reuse the same reference).

## Behavior preservation

- Every route, HTTP method, status code, JSON property name/casing and
  response shape is byte-for-byte identical before and after each task.
- No database migration is created; no persisted schema changes.
- `TsidId`/`TenantForge.BuildingBlocks` usage inside `TenantForge.Modules.Iam`
  is unaffected — the Contract project never depends on it and never will,
  because HTTP contracts only ever carry the canonical string form.
- The IAM two-method composition seam (`AddIamModule`/`UseIamModuleAsync`)
  does not change.

## Verification (all three tasks)

- `dotnet.exe build TenantForge.sln --nologo` succeeds after every task.
- `dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo`
  passes in full after every task — this is the behavior-preservation proof,
  since every existing IAM HTTP contract test still asserts the same JSON.
- B023 adds `IamContractArchitectureTests.cs`
  (`tests/integration/TenantForge.Api.IntegrationTests/`), mirroring
  `BuildingBlocksArchitectureTests.cs`, asserting:
  - `TenantForge.Modules.Iam.Contract` has zero `ProjectReference` entries
    in its `.csproj`;
  - the compiled Contract assembly references no `TenantForge.Modules.*`,
    `TenantForge.Api`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`
    or `Npgsql.*` assembly;
  - `TenantForge.Modules.Iam.csproj` contains exactly one `ProjectReference`
    to `TenantForge.Modules.Iam.Contract.csproj`;
  - the Contract assembly's exported production types are exactly the 32
    listed above, no more and no fewer (same exact-match style as
    `BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes`).
- Manual demo after each task (same shape as B018's): sign in, reload
  `/api/auth/me`, list tenants, open one tenant-scoped page (members, roles,
  invitations, audit) and confirm every response still matches its documented
  shape. Restart the API to confirm migration/seeding stays idempotent —
  this slice never touches persistence, but the check is cheap proof that
  nothing in the composition seam regressed.

## Acceptance

- All 32 listed types exist only in `TenantForge.Modules.Iam.Contract`,
  under the documented namespace-per-kind convention, as `public sealed
  record`.
- No duplicate definition remains in `TenantForge.Modules.Iam`.
- `AuthenticatedAccount`, `PaginationSupport`, and the four
  roles-internal-only records stay exactly where they are today.
- The dependency graph is exactly API → IAM → {Contract, BuildingBlocks};
  Contract has no outgoing project references.
- `IamContractArchitectureTests` (added in B023) passes and enumerates
  every type above.
- The full integration suite passes after each of the three tasks with zero
  contract/behavior change.
- `docs/modules/IAM.md` is updated in each task per its own change-impact
  checklist (Section 4's source-code map gains the Contract project; Section
  3's dependency diagram is updated in B021 and left accurate through
  B022/B023) — each task states its exact `IAM.md impact:` declaration.
