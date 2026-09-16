# IAM Contract handbook

**Read this first for IAM Contract project questions.** This document
describes current, merged `TenantForge.Modules.Iam.Contract` behavior only.
It is not a task diary: it contains no `planned`/task-status language and no
historical narrative. When it disagrees with code or tests, the code is the
source of truth — report the mismatch and verify before trusting either.

`TenantForge.Modules.Iam.Contract` exists to hold one thing: the exact HTTP
request/query/response shapes IAM has already delivered. Read
[Section 1](#1-purpose-and-strict-non-purpose) before proposing any addition.

For IAM *module* behavior (domain, persistence, auth, authorization, startup,
endpoint semantics) read the [IAM module handbook](../modules/IAM.md). This
handbook covers only the contract project the module exposes.

## Table of contents

1. [Purpose and strict non-purpose](#1-purpose-and-strict-non-purpose)
2. [Fast facts](#2-fast-facts)
3. [Dependency rule](#3-dependency-rule)
4. [Exported-type catalog](#4-exported-type-catalog)
5. [Admission checklist](#5-admission-checklist)
6. [Explicit exclusions](#6-explicit-exclusions)
7. [Change and compatibility policy](#7-change-and-compatibility-policy)
8. [Test and verification map](#8-test-and-verification-map)
9. [Decision records](#9-decision-records)
10. [Change-impact checklist](#10-change-impact-checklist)

## 1. Purpose and strict non-purpose

`TenantForge.Modules.Iam.Contract` contains only:

- IAM's already-delivered HTTP request, query and response shapes — the
  types the endpoint catalog in `docs/modules/IAM.md` Section 11 sends and
  receives (one `public sealed record` per type, one sub-namespace per
  kind).

It is **not**:

- a place for new speculative types ("might be useful for the next endpoint"
  is not evidence — see [Section 5](#5-admission-checklist));
- a `Common`, `Shared`, `Utils` or `Helpers` bucket;
- a cross-module shared-primitive home — that is
  `TenantForge.BuildingBlocks`'s job ([handbook](../building-blocks/README.md));
- a home for types that couple ASP.NET (`HttpRequest`), EF Core
  (`IQueryable`, `DbContext`) or Npgsql, or that reference an `internal`
  domain/infrastructure type;
- a shortcut for exposing IAM internals — it holds data shapes only, never
  behavior.

New code always starts in the owning module (`TenantForge.Modules.Iam`).
A type enters this project only through the admission rule in
[Section 5](#5-admission-checklist).

## 2. Fast facts

| Fact | Value |
| --- | --- |
| Project directory / assembly | `src/modules/iam/TenantForge.Modules.Iam.Contract/` (`TenantForge.Modules.Iam.Contract.csproj`, assembly `TenantForge.Modules.Iam.Contract`) |
| Target framework | `net10.0` |
| Direct framework/package dependencies | None — the `.csproj` has no `ProjectReference`, no `PackageReference`, no `FrameworkReference` (bare SDK, `Nullable`, `ImplicitUsings`) |
| Allowed reference direction | Zero outgoing; `TenantForge.Modules.Iam` is the only incoming `ProjectReference` — see [Section 3](#3-dependency-rule) |
| Exported public production type count | 32 (1 Query + 6 Requests + 25 Responses) — verified by `IamContractArchitectureTests.IamContract_ExportsOnlyTheApprovedProductionTypes` |
| Namespace layout | `TenantForge.Modules.Iam.Contract.Requests` (`Requests/`), `TenantForge.Modules.Iam.Contract.Queries` (`Queries/`), `TenantForge.Modules.Iam.Contract.Responses` (`Responses/`) — one folder per kind |
| Current consuming projects | `TenantForge.Modules.Iam` only (via one `ProjectReference`); no second module exists yet |
| Architecture test location | `tests/integration/TenantForge.Api.IntegrationTests/IamContractArchitectureTests.cs` (4 facts) |
| Handbook update declarations | `IAM Contract docs impact: updated — <sections/types>` / `IAM Contract docs impact: none — <specific reason>` — see [Section 10](#10-change-impact-checklist) |

## 3. Dependency rule

Verified against the real project files
(`IamContractArchitectureTests.IamContract_ProjectHasZeroProjectReferences`
and `IamModule_ReferencesContractExactlyOnce` re-verify this on every test
run):

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.Modules.Iam.Contract
        -> TenantForge.BuildingBlocks
```

- `src/api/TenantForge.Api/TenantForge.Api.csproj` references
  `TenantForge.Modules.Iam.csproj` only — the API host does **not** reference
  the Contract project (it has no reason to: it only calls
  `AddIamModule`/`UseIamModuleAsync`);
- `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj`
  references `TenantForge.Modules.Iam.Contract.csproj` exactly once (its other
  `ProjectReference` is `TenantForge.BuildingBlocks.csproj`);
- `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`
  has **zero** `ProjectReference` entries — and no
  `PackageReference`/`FrameworkReference` beyond the bare SDK.

Rules:

- the Contract project may never reference the API host, the IAM module,
  BuildingBlocks, or any framework package — every type it holds is a plain
  C# record with primitive/string/collection members. HTTP contracts carry
  canonical TSID strings, never `Tsid`, so the Contract project never needs
  `TenantForge.BuildingBlocks`;
  `IamContractArchitectureTests.IamContract_AssemblyDoesNotReferenceApiModulesEfOrNpgsql`
  also asserts the compiled assembly references no `TenantForge.Modules.*`,
  `TenantForge.Api`, `Microsoft.AspNetCore.*`,
  `Microsoft.EntityFrameworkCore*` or `Npgsql*` assembly;
- a future module consumes IAM shapes by adding a `ProjectReference` to
  `TenantForge.Modules.Iam.Contract` **only** — never to
  `TenantForge.Modules.Iam` itself. No such second reference exists today;
  when one appears, [Section 2](#2-fast-facts)'s consumers row and this
  section must be updated in the same task.

## 4. Exported-type catalog

One row per public production type exported by the compiled assembly
(non-compiler-generated types only, matching
`IamContractArchitectureTests.IamContract_ExportsOnlyTheApprovedProductionTypes`).
"Locked by" names the architecture-test fact that would fail if the type
were removed or renamed; JSON-shape behavior is additionally covered by the
integration test class named per group.

All 32 types share one uniform form: `public sealed record`, with
parameterized-constructor members. `string` ID members are the canonical
13-character TSID string at the HTTP boundary (see `docs/modules/IAM.md`
Section 6); status/role values cross HTTP as plain strings, never domain
enums.

### 4.1 Queries (1)

| Type | Path | Purpose | Members | Current consumer | Endpoint(s) | Locked by |
| --- | --- | --- | --- | --- | --- | --- |
| `PaginationQuery` | `Queries/PaginationQuery.cs` | Query-string binding shape for all paginated reads | `int PageNumber, int PageSize` | `features/pagination/PaginationSupport.cs` (binds it), plus `features/roles/RolesFeature.cs` | All 7 paginated endpoints in `docs/modules/IAM.md` Section 12 | `IamContract_ProjectHasZeroProjectReferences` + the 32-type roster; shape via `PaginationIntegrationTests` |

### 4.2 Requests (6)

| Type | Path | Purpose | Members | Current consumer | Endpoint(s) | Locked by |
| --- | --- | --- | --- | --- | --- | --- |
| `LoginRequest` | `Requests/LoginRequest.cs` | Login body | `string? Email, string? Password` | `features/login/LoginFeature.cs` | `POST /api/auth/login` | roster fact; shape via `LoginIntegrationTests` |
| `CreateUserRequest` | `Requests/CreateUserRequest.cs` | Platform user creation body | `string? Email, string? DisplayName, string? Password` | `features/users/UsersFeature.cs` | `POST /api/platform/users` | roster fact; shape via `UserManagementIntegrationTests` |
| `CreateTenantRequest` | `Requests/CreateTenantRequest.cs` | Tenant creation body | `string? Name, string? Slug, string? OwnerUserId` | `features/tenants/TenantsFeature.cs` | `POST /api/platform/tenants` | roster fact; shape via `TenantMembershipIntegrationTests` |
| `CreateRoleRequest` | `Requests/CreateRoleRequest.cs` | Custom tenant role creation body | `string? Name, IReadOnlyList<string>? PermissionKeys` | `features/roles/RolesFeature.cs` | `POST /api/tenants/{tenantId}/roles` | roster fact; shape via `RolePermissionIntegrationTests` |
| `UpdateRoleRequest` | `Requests/UpdateRoleRequest.cs` | Role permission replacement body | `IReadOnlyList<string>? PermissionKeys` | `features/roles/RolesFeature.cs` | `PUT /api/tenants/{tenantId}/roles/{roleId}` | roster fact; shape via `RolePermissionIntegrationTests` |
| `CreateInvitationRequest` | `Requests/CreateInvitationRequest.cs` | Invitation creation body | `string? Email, string? Role` | `features/invitations/InvitationsFeature.cs` | `POST /api/tenants/{tenantId}/invitations` | roster fact; shape via `InvitationAuditIntegrationTests` |

### 4.3 Responses (25)

| Type | Path | Purpose | Members | Current consumer | Endpoint(s) | Locked by |
| --- | --- | --- | --- | --- | --- | --- |
| `LoginResponse` | `Responses/LoginResponse.cs` | Login result | `string AccessToken, string ExpiresAtUtc, LoginUserResponse User` | `features/login/LoginFeature.cs` | `POST /api/auth/login` | roster fact; shape via `LoginIntegrationTests` |
| `LoginUserResponse` | `Responses/LoginUserResponse.cs` | User echo inside `LoginResponse` | `string Id, string Email, string DisplayName, bool IsPlatformAdmin` | nested in `LoginResponse` (`features/login/LoginFeature.cs`) | `POST /api/auth/login` (`user`) | roster fact; shape via `LoginIntegrationTests` |
| `CurrentAccountResponse` | `Responses/CurrentAccountResponse.cs` | Caller identity echo | `string Id, string Email, string DisplayName, bool IsPlatformAdmin` | `features/account/CurrentAccountFeature.cs` | `GET /api/auth/me` | roster fact; shape via `CurrentAccountIntegrationTests` |
| `TenantDiscoveryResponse` | `Responses/TenantDiscoveryResponse.cs` | Paged list of the caller's tenants | `IReadOnlyList<DiscoveredTenantResponse> Tenants, PaginationMetadata Pagination` | `features/account/TenantDiscoveryFeature.cs` | `GET /api/auth/me/tenants` | roster fact; shape via `TenantDiscoveryIntegrationTests` |
| `DiscoveredTenantResponse` | `Responses/DiscoveredTenantResponse.cs` | One discovered tenant | `string Id, string Name, string Slug, string Status, string MembershipRole` | nested in `TenantDiscoveryResponse` | `GET /api/auth/me/tenants` (`tenants[]`) | roster fact; shape via `TenantDiscoveryIntegrationTests` |
| `DashboardSummaryResponse` | `Responses/DashboardSummaryResponse.cs` | Platform summary object | `string Environment, string ApiStatus, int PlatformAdminCount, string GeneratedAtUtc` | `features/dashboard/DashboardSummaryFeature.cs` | `GET /api/platform/dashboard-summary` | roster fact; shape via `DashboardSummaryIntegrationTests` |
| `UsersListResponse` | `Responses/UsersListResponse.cs` | Paged platform user list | `IReadOnlyList<UserResponse> Users, PaginationMetadata Pagination` | `features/users/UsersFeature.cs` | `GET /api/platform/users` | roster fact; shape via `UserManagementIntegrationTests` |
| `UserResponse` | `Responses/UserResponse.cs` | One platform account | `string Id, string Email, string DisplayName, string Status, bool IsPlatformAdmin, string CreatedAtUtc` | nested in `UsersListResponse`; also the `POST` created user | `GET /api/platform/users` (`users[]`), `POST /api/platform/users` | roster fact; shape via `UserManagementIntegrationTests` |
| `TenantListResponse` | `Responses/TenantListResponse.cs` | Paged platform tenant list | `IReadOnlyList<TenantSummaryResponse> Tenants, PaginationMetadata Pagination` | `features/tenants/TenantsFeature.cs` | `GET /api/platform/tenants` | roster fact; shape via `TenantMembershipIntegrationTests` |
| `TenantSummaryResponse` | `Responses/TenantSummaryResponse.cs` | One tenant with member count | `string Id, string Name, string Slug, string Status, int MemberCount, string CreatedAtUtc` | nested in `TenantListResponse`; also the `POST` created tenant | `GET /api/platform/tenants` (`tenants[]`), `POST /api/platform/tenants` | roster fact; shape via `TenantMembershipIntegrationTests` |
| `TenantMembersResponse` | `Responses/TenantMembersResponse.cs` | Paged tenant member list with tenant context | `TenantContextResponse Tenant, IReadOnlyList<TenantMemberResponse> Members, PaginationMetadata Pagination` | `features/tenantmembers/TenantMembersFeature.cs` | `GET /api/tenants/{tenantId}/members` | roster fact; shape via `TenantMembershipIntegrationTests` |
| `TenantContextResponse` | `Responses/TenantContextResponse.cs` | Tenant header inside member/tenant responses | `string Id, string Name, string Slug, string Status` | nested in `TenantMembersResponse` | `GET /api/tenants/{tenantId}/members` (`tenant`) | roster fact; shape via `TenantMembershipIntegrationTests` |
| `TenantMemberResponse` | `Responses/TenantMemberResponse.cs` | One tenant member | `string Id, string UserId, string Email, string DisplayName, string Role, string CreatedAtUtc` | nested in `TenantMembersResponse` | `GET /api/tenants/{tenantId}/members` (`members[]`) | roster fact; shape via `TenantMembershipIntegrationTests` |
| `PermissionCatalogResponse` | `Responses/PermissionCatalogResponse.cs` | Static permission catalog root | `IReadOnlyList<PermissionGroupResponse> Groups` | `features/roles/RolesFeature.cs` (`CatalogGroups`) | `GET /api/permissions/catalog` | roster fact; shape via `RolePermissionIntegrationTests` |
| `PermissionGroupResponse` | `Responses/PermissionGroupResponse.cs` | One catalog group | `string Id, string Label, string Description, IReadOnlyList<PermissionResponse> Permissions` | nested in `PermissionCatalogResponse` (`RolesFeature.CatalogGroups`) | `GET /api/permissions/catalog` (`groups[]`) | roster fact; shape via `RolePermissionIntegrationTests` |
| `PermissionResponse` | `Responses/PermissionResponse.cs` | One permission key entry | `string Key, string Label, string Description, string Kind` | nested in `PermissionGroupResponse` (`RolesFeature.CatalogGroups`) | `GET /api/permissions/catalog` (`groups[].permissions[]`) | roster fact; shape via `RolePermissionIntegrationTests` |
| `TenantRolesResponse` | `Responses/TenantRolesResponse.cs` | Tenant role list (role-assignment result) | `IReadOnlyList<TenantRoleResponse> Roles` | `features/roles/RolesFeature.cs` | `PUT /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`, `DELETE .../roles/{roleId}` (both return the current role list) | roster fact; shape via `RolePermissionIntegrationTests` |
| `PagedTenantRolesResponse` | `Responses/PagedTenantRolesResponse.cs` | Paged tenant role list | `IReadOnlyList<TenantRoleResponse> Roles, PaginationMetadata Pagination` | `features/roles/RolesFeature.cs` | `GET /api/tenants/{tenantId}/roles` | roster fact; shape via `RolePermissionIntegrationTests` |
| `TenantRoleResponse` | `Responses/TenantRoleResponse.cs` | One tenant role with assignments | `string Id, string Name, string Description, string Kind, IReadOnlyList<string> PermissionKeys, IReadOnlyList<string> MemberIds, string CreatedAtUtc, string UpdatedAtUtc` | nested in `TenantRolesResponse`/`PagedTenantRolesResponse`; also the `POST` created role | `GET /api/tenants/{tenantId}/roles` (`roles[]`), `POST /api/tenants/{tenantId}/roles`, `PUT .../roles/{roleId}` | roster fact; shape via `RolePermissionIntegrationTests` |
| `ResolvedPermissionsResponse` | `Responses/ResolvedPermissionsResponse.cs` | Caller's effective permission union | `IReadOnlyList<string> Permissions` | `features/roles/RolesFeature.cs` | `GET /api/tenants/{tenantId}/me/permissions` | roster fact; shape via `RolePermissionIntegrationTests` |
| `InvitationListResponse` | `Responses/InvitationListResponse.cs` | Paged pending invitation list | `IReadOnlyList<InvitationResponse> Invitations, PaginationMetadata Pagination` | `features/invitations/InvitationsFeature.cs` | `GET /api/tenants/{tenantId}/invitations` | roster fact; shape via `InvitationAuditIntegrationTests` |
| `InvitationResponse` | `Responses/InvitationResponse.cs` | One pending invitation | `string Id, string Email, string Role, string Status, string ExpiresAtUtc, string CreatedAtUtc` | nested in `InvitationListResponse`; also the `POST` created invitation | `GET /api/tenants/{tenantId}/invitations` (`invitations[]`), `POST /api/tenants/{tenantId}/invitations` | roster fact; shape via `InvitationAuditIntegrationTests` |
| `AuditListResponse` | `Responses/AuditListResponse.cs` | Paged audit event list | `IReadOnlyList<AuditEventResponse> Events, PaginationMetadata Pagination` | `features/audit/AuditFeature.cs` | `GET /api/tenants/{tenantId}/audit` | roster fact; shape via `InvitationAuditIntegrationTests` |
| `AuditEventResponse` | `Responses/AuditEventResponse.cs` | One audit event | `string Id, string Actor, string ActorEmail, string Action, string Target, string Details, string CreatedAtUtc` | nested in `AuditListResponse` | `GET /api/tenants/{tenantId}/audit` (`events[]`) | roster fact; shape via `InvitationAuditIntegrationTests` |
| `PaginationMetadata` | `Responses/PaginationMetadata.cs` | Paged-response metadata block | `int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage` | `features/pagination/PaginationSupport.cs`; embedded in every paged list response | All 7 paginated endpoints (`pagination`) | roster fact; shape via `PaginationIntegrationTests` |

No other public production type is exported. If delivered code ever adds a
33rd exported type without updating this catalog and the hand-enumerated
roster in `IamContractArchitectureTests`, that is a failed build and a
documentation gap, not evidence the type is undocumented-but-fine.

## 5. Admission checklist

A type belongs in this project when **both** are true:

1. it is already part of a real, delivered IAM HTTP request or response
   shape — present in `docs/modules/IAM.md` Section 11's endpoint catalog,
   directly or as a nested type of a response listed there; **and**
2. it carries no ASP.NET (`HttpRequest`, `IEndpointRouteBuilder`), EF Core
   (`IQueryable`, `DbContext`) or Npgsql dependency, and no reference to an
   `internal` domain/infrastructure type.

For every proposed addition, also complete this table before the change.
Missing any answer means: reject, keep the type in `TenantForge.Modules.Iam`.

| Evidence | Required answer |
| --- | --- |
| Endpoint | Which delivered `docs/modules/IAM.md` Section 11 route sends or receives this shape? |
| Shape fidelity | Exact member names, order and types — a move must be byte-for-byte identical JSON, not a reshape |
| Dependencies | Proof of zero ASP.NET/EF/Npgsql/internal-domain references in the type file |
| Roster | The new type added to `IamContractArchitectureTests`'s 32-type roster **and** to [Section 4](#4-exported-type-catalog) in the same change |
| Compatibility | Source/binary/transport impact on `TenantForge.Modules.Iam` and any other future referencing project |
| Tests | Which integration test class proves the JSON shape; which architecture fact locks the name |
| Alternatives | Why keeping it in the owning feature file is insufficient |

"Cleaner", "reusable", "a future module may need it" and "best practice" are
**not** sufficient evidence for any row. There is no second module today;
the "delivered HTTP shape" bar is the only branch that can be met.

## 6. Explicit exclusions

Post-S23 exclusions and their current owners — each stays in
`TenantForge.Modules.Iam` for a concrete reason, not a promise of future
extraction:

| Excluded concern | Current owner | Why it stays out |
| --- | --- | --- |
| `AuthenticatedAccount` | `src/modules/iam/TenantForge.Modules.Iam/features/login/AuthenticatedAccount.cs` | The credential-check result the login feature mints a token from — not itself serialized to an HTTP caller; `LoginUserResponse` is the response shape |
| `PaginationSupport` (`TryBind`, `PageAsync`, `Parse`) | `src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs` (`internal`) | `HttpRequest`-binding and `IQueryable`/EF execution — infrastructure, not a contract shape. Only the two plain records (`PaginationQuery`, `PaginationMetadata`) met the admission rule and moved |
| `TenantAccess`, `ActorSnapshot`, `AssignmentValidation`, `RemovedAssignment` | `src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs` (`internal sealed`) | Intermediate handler-only computation records; never returned as-is to an HTTP caller |
| `CatalogGroups` catalog data | `src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs` (`private static`) | The permission catalog's content (labels, keys, kinds) is IAM business data, not a transport shape — only the response records that carry it are contract types |
| `UserResponse.FromAccount` (and any feature-local mapper) | `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs` (`private static`) | Maps the `internal` `Account` entity to a contract record — a reference to an internal domain type disqualifies it by rule 2 |
| Every domain entity and enum (`Account`, `Tenant`, `TenantMembership`, `TenantRole`, `TenantMemberRoleAssignment`, `TenantInvitation`, `AuditEvent`, `AccountStatus`, `TenantStatus`, `TenantMembershipRole`, …) | `src/modules/iam/TenantForge.Modules.Iam/domain/` | Persistence/domain model; every current response already re-expresses status/role as a plain string, so no domain enum is a contract type |
| `TsidId` / TSID handling | `src/building-blocks/TenantForge.BuildingBlocks/Identifiers/TsidId.cs` | A cross-module shared primitive — BuildingBlocks' job, not a module contract's. Contract types carry the canonical string; they never reference `Tsid` |

`Commands/` and `Enums/` sub-namespaces do not exist: no current IAM request
is modeled as a distinct command object, and no response serializes a domain
enum directly. Create either folder only in a task that adds a real type
that belongs there, through [Section 5](#5-admission-checklist).

## 7. Change and compatibility policy

Classify every change to this project into exactly one of these classes, and
follow the required review for that class:

| Class | Example | Required review |
| --- | --- | --- |
| File/formatting-only, same JSON | Renaming a private helper inside the IAM module that builds a response; reformatting a record file | No handbook change required; still record `IAM Contract docs impact: none — <reason>` |
| Additive member | Adding an optional response property a new endpoint needs | Update [Section 4](#4-exported-type-catalog) + the hand-enumerated roster in `IamContractArchitectureTests` in the same change; update `docs/modules/IAM.md` Section 11's endpoint row |
| Member removal/renaming/type change | Renaming `TenantRoleResponse.MemberIds` | Breaking for every consumer of the JSON shape (IAM's own features, the test suite, and the frontend): enumerate and re-verify every consumer before implementation; update [Section 4](#4-exported-type-catalog), the roster, and `docs/modules/IAM.md` |
| New exported type | A new request/response record for a new endpoint | Complete [Section 5](#5-admission-checklist); add the catalog row + roster entry in the same change; update `docs/modules/IAM.md`'s endpoint catalog |
| Reference/package change | Any new `ProjectReference`, `PackageReference` or `FrameworkReference` in the Contract `.csproj` | Rejected by default — it violates [Section 3](#3-dependency-rule) and fails `IamContractArchitectureTests`. Reintroduce only with a handbook update of [Section 2](#2-fast-facts) + [Section 3](#3-dependency-rule) + a reasoned relaxation of the architecture test, explicitly reviewed |
| New incoming reference | A second module referencing the Contract project | Update [Section 2](#2-fast-facts) (consumers) + [Section 3](#3-dependency-rule); the roster and zero-outgoing facts are unaffected |
| Identifier/serialization change | Any change to how an ID or timestamp string is formatted in a contract type | Update [Section 4](#4-exported-type-catalog) + `docs/modules/IAM.md`'s identity contract (Section 6) and endpoint catalog in the same task |

There is no semantic-versioning or package-publication system; this is an
internal project-reference library consumed only through in-repo
`ProjectReference`. The hand-enumerated roster (never computed) is the
mechanism that makes any surface change a deliberate, visible act.

## 8. Test and verification map

| Contract concern | Test class/file |
| --- | --- |
| Zero `ProjectReference` entries in the Contract `.csproj` | `IamContractArchitectureTests.IamContract_ProjectHasZeroProjectReferences` |
| No `TenantForge.Modules.*` / `TenantForge.Api` / `Microsoft.AspNetCore.*` / `Microsoft.EntityFrameworkCore*` / `Npgsql*` assembly reference from the compiled Contract assembly | `IamContractArchitectureTests.IamContract_AssemblyDoesNotReferenceApiModulesEfOrNpgsql` |
| Exactly one incoming `ProjectReference`, from `TenantForge.Modules.Iam` | `IamContractArchitectureTests.IamModule_ReferencesContractExactlyOnce` |
| Exact 32-type exported surface (hand-enumerated roster) | `IamContractArchitectureTests.IamContract_ExportsOnlyTheApprovedProductionTypes` |
| Login JSON shape (request + response) | `LoginIntegrationTests.cs` |
| Account echo + tenant discovery shapes | `CurrentAccountIntegrationTests.cs`, `TenantDiscoveryIntegrationTests.cs` |
| Dashboard summary shape | `DashboardSummaryIntegrationTests.cs` |
| Platform users/tenants shapes (list + create) | `UserManagementIntegrationTests.cs`, `TenantMembershipIntegrationTests.cs` |
| Tenant members shape (context + list) | `TenantMembershipIntegrationTests.cs`, `TenantIsolationIntegrationTests.cs` |
| Permission catalog, role list/CRUD/assignment, resolved permissions shapes | `RolePermissionIntegrationTests.cs` |
| Invitation + audit shapes (list + create) | `InvitationAuditIntegrationTests.cs` |
| `PaginationQuery`/`PaginationMetadata` behavior and metadata values | `PaginationIntegrationTests.cs` |
| Behavior-preservation proof for this project as a whole | The full `TenantForge.Api.IntegrationTests` suite — every IAM HTTP contract test asserts the same JSON it asserted before the Contract project existed |

Commands:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Focused filter:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~IamContractArchitectureTests"
```

## 9. Decision records

Compact current-state decisions, not a chronological changelog. A superseded
decision is replaced here, not appended alongside the old one.

| Decision | Reason | Revisit only when |
| --- | --- | --- |
| Name the project `TenantForge.Modules.Iam.Contract`, beside `TenantForge.Modules.Iam` — not `Common`/`Shared` | The `Module` in the name keeps ownership visible: this is IAM's public surface, and the admission rule ([Section 5](#5-admission-checklist)) keeps it from becoming a dumping ground | Never for convenience |
| A module-owned Contract project **alongside** `TenantForge.BuildingBlocks`, not folded into it | Different problem: BuildingBlocks holds *cross-module shared primitives* (meaningful without any business module: `IModuleConfig`, `TsidId`); the Contract project holds *what one specific module promises the outside world* — every type here only exists because an IAM endpoint delivered it. Folding IAM's HTTP shapes into BuildingBlocks would make a shared-primitive home carry module-specific business data | A real second module exists and a *genuinely shared* shape between two modules emerges; even then the shape stays in the owning module's Contract project unless it stops being IAM-specific |
| Created before a second module exists | Every type was already a delivered, exercised HTTP shape — the move relocated existing code, it invented no new abstraction; the convention is what lets a future module depend on IAM shapes without pulling in EF Core, Npgsql, migrations, seeding and feature-handler internals | — (the decision stands or is replaced, it does not accumulate) |
| Zero outgoing references, including on BuildingBlocks | HTTP contracts carry canonical TSID strings, never `Tsid`, so the Contract project can stay dependency-free and compilable in the smallest possible context; `IamContractArchitectureTests` enforces this | A delivered contract type genuinely needs a non-string member type that no framework-free project provides (has not happened; none of the 32 types needs it) |
| One `public sealed record` per type, one folder per kind, member order preserved | Uniform `sealed` prevents accidental subclassing at a boundary; one folder per kind keeps namespaces small; preserving member order/names made the S23 move a pure relocation with byte-identical JSON | Never for a pure move; a reshape is a [Section 7](#7-change-and-compatibility-policy) breaking change |
| The 32-type roster in the architecture test is hand-enumerated, never computed | The test must *assert* the surface; computing it from the assembly would make the test tautological and would not catch an added type | Never — recomputation is the failure mode the test exists to prevent |
| The roster is ordered ordinally (`Queries` before `Requests`), not in folder-convention order | The assertion compares with `StringComparer.Ordinal`; the entries are verbatim the S23 list, re-ordered to match the comparator | A change to the comparator, with the table above re-sorted to match |

## 10. Change-impact checklist

| Changed area | Required handbook action |
| --- | --- |
| public type added/removed/renamed | Update [Section 4](#4-exported-type-catalog) + the hand-enumerated roster in `IamContractArchitectureTests` + [Section 2](#2-fast-facts) count, in the same change |
| member added/removed/retyped on any contract type | Update [Section 4](#4-exported-type-catalog) + [Section 7](#7-change-and-compatibility-policy) if it reverses a documented decision; also check `docs/modules/IAM.md` (endpoint catalog row, pagination section) |
| package/framework/project reference | Update [Section 2](#2-fast-facts) + [Section 3](#3-dependency-rule) + [Section 9](#9-decision-records); requires relaxing `IamContractArchitectureTests` deliberately, not silently |
| new incoming consumer project | Update [Section 2](#2-fast-facts) (consumers) + [Section 3](#3-dependency-rule) |
| endpoint added/removed in IAM | Update [Section 4](#4-exported-type-catalog) endpoint columns + `docs/modules/IAM.md` Section 11 in the same task |
| exclusion becomes admitted (or admitted type becomes excluded) | Update [Section 6](#6-explicit-exclusions) + [Section 4](#4-exported-type-catalog) + record completed [Section 5](#5-admission-checklist) evidence |
| test path/command changed | Update [Section 8](#8-test-and-verification-map) |
| no documented fact changed | Record the exact justified no-impact declaration below |

Use exactly one of these two declarations in self-review and the PR body for
every task that touches
`src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a consumer's
reference to it:

```text
IAM Contract docs impact: updated — <sections/types>
IAM Contract docs impact: none — <specific reason>
```

A vague "docs not needed" does not satisfy this gate. Cross-check any change
to an exported type against `docs/modules/IAM.md` as well, since IAM's
endpoint catalog documents the same shapes; a TSID-format change additionally
triggers `docs/building-blocks/README.md`'s checklist, since
`TenantForge.BuildingBlocks` owns the identifier seam the contract strings
are formatted through.
