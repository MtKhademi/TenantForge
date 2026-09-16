---
id: B022
slice: S23
title: Move users, tenants and tenant-member contract types into the IAM Contract project
agent: backend-mentor
source: tasks/slices/023-iam-contract-separation.md
---

# Objective

Continue the IAM contract separation started in B021: move the platform
users, platform tenants and tenant-members request/response types into
the existing `TenantForge.Modules.Iam.Contract` project. No route, JSON
shape, status code or persisted behavior changes.

# Context

Read `docs/modules/IAM.md` completely (this task changes
`src/modules/iam/**`). Read the complete
`tasks/slices/023-iam-contract-separation.md` — it is the authoritative
contract for this task. Read `tasks/backend/B021-iam-contract-foundation.md`
if it still exists (it will have been deleted once B021 reached `done`; if
so, read its ledger row's delivered commit/PR instead, or simply inspect
`src/modules/iam/TenantForge.Modules.Iam.Contract/` directly — the code is
the source of truth once B021 is merged).

B021 must be `done` on `main` before starting this task — it created the
`TenantForge.Modules.Iam.Contract` project, the `Requests/`, `Queries/` and
`Responses/` folders/namespaces, and the one `ProjectReference` from
`TenantForge.Modules.Iam`. This task reuses that project and reference; it
does not create a second one.

# Scope

Move exactly these nine types, each becoming `public sealed record` with
identical members, using the same recipe B021 used (declare in the new
location under the matching `Requests`/`Responses` namespace, delete the
old declaration, update every referencing file's `using`):

| Type | From | To |
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

Before editing, run
`grep -rn "CreateUserRequest\|UsersListResponse\|UserResponse\|CreateTenantRequest\|TenantListResponse\|TenantSummaryResponse\|TenantMembersResponse\|TenantContextResponse\|TenantMemberResponse" src/modules/iam/`
to find every reference (each feature file plus any test-visible usage
inside `src/modules/iam` — integration tests live outside this module and
are unaffected because they only assert JSON, not C# types). After editing,
run the same `grep` and confirm every remaining hit is a usage (via the new
`using TenantForge.Modules.Iam.Contract.Requests;` /
`using TenantForge.Modules.Iam.Contract.Responses;`), never a second
declaration.

`UserResponse` and `TenantSummaryResponse` are the two response shapes
most likely to carry more fields than their name suggests (member counts,
role flags, etc. — read the actual current record definition in
`UsersFeature.cs`/`TenantsFeature.cs` before writing the new file; do not
guess the member list from memory or from this Spec).

Do not touch `features/roles/**`, `features/invitations/**`,
`features/audit/**`, `features/pagination/**`, `features/login/**`,
`features/account/**`, `features/dashboard/**` — pagination/login/account/
dashboard already moved in B021; roles/invitations/audit move in B023.

# Acceptance

- The solution builds; `TenantForge.Modules.Iam.csproj` still has exactly
  one `ProjectReference` to `TenantForge.Modules.Iam.Contract.csproj` (no
  new project reference is added in this task).
- The nine listed types exist only in `TenantForge.Modules.Iam.Contract`,
  as `public sealed record`, under the correct namespace; no duplicate
  remains in `TenantForge.Modules.Iam`.
- Every existing IAM route touched by this task
  (`GET`/`POST /api/platform/users`, `GET`/`POST /api/platform/tenants`,
  `GET /api/tenants/{tenantId}/members`) returns byte-for-byte the same
  JSON shape as before this task — proven by the full integration suite.
- `docs/modules/IAM.md`: state the exact declaration. If nothing in
  Sections 1–16 needs a factual update beyond what B021 already recorded
  (routes, request/response shapes and paths are unchanged, only their
  C# location moved), state
  `IAM.md impact: none — namespace/location move only, no documented
  route/shape/config fact changed`. If you find the source-code map
  (Section 4) needs a refresh for the moved files' new paths, update it and
  state `IAM.md impact: updated — Section 4`.

# Verification

Automated:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Run the full suite, not a filtered subset — `UserManagementIntegrationTests`
and `TenantMembershipIntegrationTests` are the most directly relevant
classes, but the whole suite is the actual acceptance bar.

Manual:

- As platform admin: list platform users (`GET /api/platform/users`),
  create one (`POST /api/platform/users`), list platform tenants
  (`GET /api/platform/tenants`), create one with an owner
  (`POST /api/platform/tenants`).
- As a member of that tenant: `GET /api/tenants/{tenantId}/members` and
  confirm the response shape (tenant summary + paged member list) is
  unchanged.

# Lifecycle

Add row `B022` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B021`, and Spec link
`tasks/backend/B022-iam-contract-directory-features.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/023-iam-contract-separation.md` is the permanent
record and is never deleted.
