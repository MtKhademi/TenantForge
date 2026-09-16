---
id: B023
slice: S23
title: Move roles, invitations and audit contract types, then lock the IAM Contract project's exported surface
agent: backend-mentor
source: tasks/slices/023-iam-contract-separation.md
---

# Objective

Finish the IAM contract separation started in B021/B022: move the
remaining fourteen roles/invitations/audit request/response types into
`TenantForge.Modules.Iam.Contract`, then add an architecture test that
locks the project's exact exported surface and dependency direction — the
same proof `BuildingBlocksArchitectureTests` gives `TenantForge.BuildingBlocks`.
No route, JSON shape, status code or persisted behavior changes.

# Context

Read `docs/modules/IAM.md` completely (this task changes
`src/modules/iam/**`). Read the complete
`tasks/slices/023-iam-contract-separation.md` — it is the authoritative
contract for this task, including the full 32-type roster the new
architecture test must assert. Read
`tests/integration/TenantForge.Api.IntegrationTests/BuildingBlocksArchitectureTests.cs`
before writing the new test file — mirror its structure and its use of
`XDocument`-based `.csproj` inspection and `Assembly.GetExportedTypes()`;
do not reinvent the approach.

B021 and B022 must both be `done` on `main` before starting: they created
the `TenantForge.Modules.Iam.Contract` project and moved eighteen of the
thirty-two total contract types. This task moves the last fourteen and
then adds the test that proves the whole project's surface is exactly
what was intended across all three tasks.

`features/roles/RolesFeature.cs` is the largest file this slice touches —
it currently mixes nine contract types with handler logic and four
handler-only records (`TenantAccess`, `ActorSnapshot`,
`AssignmentValidation`, `RemovedAssignment`) that stay exactly where they
are. Read the whole file before editing so you can tell the two groups
apart with certainty; do not move a type you are not fully sure is one of
the nine listed below.

# Scope

1. Move exactly these fourteen types, each becoming `public sealed record`
   with identical members:

   | Type | From | To |
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

   `RolesFeature.CatalogGroups` (the static permission catalog data)
   constructs `PermissionGroupResponse`/`PermissionResponse` instances —
   after the move it references them via
   `using TenantForge.Modules.Iam.Contract.Responses;`; the catalog data
   itself stays in `RolesFeature.cs` (it is business data, not a type
   definition).

2. Leave `TenantAccess`, `ActorSnapshot`, `AssignmentValidation` and
   `RemovedAssignment` exactly where they are, in `RolesFeature.cs`.
3. Before editing, run
   `grep -rn "CreateRoleRequest\|UpdateRoleRequest\|PermissionCatalogResponse\|PermissionGroupResponse\|PermissionResponse\|TenantRolesResponse\|PagedTenantRolesResponse\|TenantRoleResponse\|ResolvedPermissionsResponse\|CreateInvitationRequest\|InvitationListResponse\|InvitationResponse\|AuditListResponse\|AuditEventResponse" src/modules/iam/`
   to find every reference; after editing, run it again and confirm every
   hit is a usage, never a second declaration.
4. Add `tests/integration/TenantForge.Api.IntegrationTests/IamContractArchitectureTests.cs`
   with (at minimum) these facts, each its own `[Fact]`:
   - `TenantForge.Modules.Iam.Contract.csproj` has zero `<ProjectReference>`
     entries (parse the `.csproj` with `XDocument`, same technique as
     `BuildingBlocksArchitectureTests.ProjectReferences`).
   - The compiled `TenantForge.Modules.Iam.Contract` assembly's
     `GetReferencedAssemblies()` contains no name starting with
     `TenantForge.Modules.`, `TenantForge.Api`, `Microsoft.AspNetCore.`,
     `Microsoft.EntityFrameworkCore.` or `Npgsql.`.
   - `TenantForge.Modules.Iam.csproj` contains exactly one
     `<ProjectReference>` to
     `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`.
   - The Contract assembly's non-compiler-generated exported types
     (`GetExportedTypes()`, filtered like
     `BuildingBlocksArchitectureTests.IsCompilerGenerated`), sorted
     ordinally, equal exactly this 32-entry list (fully qualified names —
     copy this list verbatim into the test, do not compute it dynamically):
     `TenantForge.Modules.Iam.Contract.Requests.CreateInvitationRequest`,
     `TenantForge.Modules.Iam.Contract.Requests.CreateRoleRequest`,
     `TenantForge.Modules.Iam.Contract.Requests.CreateTenantRequest`,
     `TenantForge.Modules.Iam.Contract.Requests.CreateUserRequest`,
     `TenantForge.Modules.Iam.Contract.Requests.LoginRequest`,
     `TenantForge.Modules.Iam.Contract.Requests.UpdateRoleRequest`,
     `TenantForge.Modules.Iam.Contract.Queries.PaginationQuery`,
     `TenantForge.Modules.Iam.Contract.Responses.AuditEventResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.AuditListResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.CurrentAccountResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.DashboardSummaryResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.DiscoveredTenantResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.InvitationListResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.InvitationResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.LoginResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.LoginUserResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.PagedTenantRolesResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.PaginationMetadata`,
     `TenantForge.Modules.Iam.Contract.Responses.PermissionCatalogResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.PermissionGroupResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.PermissionResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.ResolvedPermissionsResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantContextResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantDiscoveryResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantListResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantMemberResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantMembersResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantRoleResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantRolesResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.TenantSummaryResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.UserResponse`,
     `TenantForge.Modules.Iam.Contract.Responses.UsersListResponse`.

     If your own build produces a 33rd exported type or fewer than 32, stop
     and reconcile against the table in
     `tasks/slices/023-iam-contract-separation.md` before writing the test
     — do not silently add an entry to make the test pass.

# Acceptance

- All fourteen listed types exist only in `TenantForge.Modules.Iam.Contract`;
  the four roles-internal-only records remain in `RolesFeature.cs`.
- `IamContractArchitectureTests` passes and asserts the complete 32-type
  roster, the zero-outgoing-reference rule, the assembly-reference
  denylist and the exactly-one-incoming-reference rule.
- The full integration suite passes with zero behavior change on every
  roles/invitations/audit route.
- `docs/modules/IAM.md` Section 4 (source-code map) and Section 14 (test
  map) both mention `IamContractArchitectureTests`/the completed Contract
  project layout. State
  `IAM.md impact: updated — Section 4, Section 14` in self-review and the
  PR body.

# Verification

Automated:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Focused filter for the new test plus the most relevant existing classes:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~IamContractArchitectureTests|FullyQualifiedName~RolePermissionIntegrationTests|FullyQualifiedName~InvitationAuditIntegrationTests"
```

Run the full suite too — the focused filter is a fast first check, not a
substitute for it.

Manual:

- As a tenant owner: fetch the permission catalog
  (`GET /api/permissions/catalog`), list tenant roles, create a custom
  role, assign/unassign it to a member, resolve `me/permissions`, create
  an invitation, list invitations, and fetch the tenant audit log. Confirm
  every response shape is byte-for-byte unchanged.

# Lifecycle

Add row `B023` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B022`, and Spec link
`tasks/backend/B023-iam-contract-roles-invitations-audit.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/023-iam-contract-separation.md` is the permanent
record and is never deleted.
