---
id: B021
slice: S23
title: Create the IAM Contract project and move its pagination, login, account and dashboard types
agent: backend-mentor
source: tasks/slices/023-iam-contract-separation.md
---

# Objective

Create `TenantForge.Modules.Iam.Contract`, a new class library that will
hold every HTTP-facing request/query/response type IAM exposes, and move
the first nine types into it: the two pagination contract types, and the
login, account and dashboard request/response types. `TenantForge.Modules.Iam`
references the new project. No route, JSON shape, status code or persisted
behavior changes.

# Context

Read `docs/modules/IAM.md` completely before touching any file (this task
changes `src/modules/iam/**` and adds an IAM contract, so IAM.md's own
read-first rule applies). Read the complete
`tasks/slices/023-iam-contract-separation.md` — it is the authoritative
contract for this task and the two that follow it (B022, B023); this Spec
only restates the slice of that document that belongs to B021.

`docs/building-blocks/README.md` (B020/S22) is the closest existing
precedent for what you are about to build: a small, deliberately bounded
project with an explicit admission rule and an architecture test that
locks its exact exported surface. Skim it for tone and rigor, but do not
touch `TenantForge.BuildingBlocks` in this task — it is unaffected.

Today, IAM's request/response DTOs live inline inside each feature file
(mostly as `internal sealed record`, a few as plain `public record`) and
are only reachable outside the assembly through
`InternalsVisibleTo("TenantForge.Api.IntegrationTests")`. There is no
second module yet to consume them — this task's purpose is establishing
the convention and moving the first, smallest batch of types, not proving
a real second consumer exists (the slice document explains why this is not
the same thing as a premature/speculative abstraction).

# Scope

1. Create `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`:
   - `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`,
     `<ImplicitUsings>enable</ImplicitUsings>`.
   - No `PackageReference`, no `FrameworkReference`, no `ProjectReference`.
   - Add it to `TenantForge.sln` (same pattern as the existing
     `TenantForge.BuildingBlocks` entry).
2. Add exactly one `<ProjectReference>` from
   `src/modules/iam/TenantForge.Modules.Iam/TenantForge.Modules.Iam.csproj`
   to the new `TenantForge.Modules.Iam.Contract.csproj` (alongside its
   existing `TenantForge.BuildingBlocks` reference — do not remove that
   one).
3. Move exactly these nine types, each becoming `public sealed record`
   with identical members, using the recipe in
   `tasks/slices/023-iam-contract-separation.md` ("Mechanical move
   recipe"):

   | Type | From | To |
   | --- | --- | --- |
   | `PaginationQuery` | `features/pagination/PaginationSupport.cs` | `Queries/PaginationQuery.cs` (`TenantForge.Modules.Iam.Contract.Queries`) |
   | `PaginationMetadata` | `features/pagination/PaginationSupport.cs` | `Responses/PaginationMetadata.cs` (`TenantForge.Modules.Iam.Contract.Responses`) |
   | `LoginRequest` | `features/login/LoginRequest.cs` | `Requests/LoginRequest.cs` |
   | `LoginResponse` | `features/login/LoginResponse.cs` | `Responses/LoginResponse.cs` |
   | `LoginUserResponse` | `features/login/LoginResponse.cs` | `Responses/LoginUserResponse.cs` |
   | `CurrentAccountResponse` | `features/account/CurrentAccountResponse.cs` | `Responses/CurrentAccountResponse.cs` |
   | `TenantDiscoveryResponse` | `features/account/TenantDiscoveryFeature.cs` | `Responses/TenantDiscoveryResponse.cs` |
   | `DiscoveredTenantResponse` | `features/account/TenantDiscoveryFeature.cs` | `Responses/DiscoveredTenantResponse.cs` |
   | `DashboardSummaryResponse` | `features/dashboard/DashboardSummaryResponse.cs` | `Responses/DashboardSummaryResponse.cs` |

   `PaginationQuery.Offset` (the computed property) moves with the record —
   it is pure arithmetic on the record's own fields, not an ASP.NET/EF
   dependency. `PaginationMetadata.From(PaginationQuery, int)` moves with
   the record for the same reason.
4. Update every file that referenced a moved type (at minimum:
   `features/pagination/PaginationSupport.cs`,
   `features/login/LoginFeature.cs`, `features/login/JwtIssuer.cs` if it
   references `LoginResponse`/`LoginUserResponse`,
   `features/account/CurrentAccountFeature.cs`,
   `features/account/TenantDiscoveryFeature.cs`,
   `features/dashboard/DashboardSummaryFeature.cs`, and any other feature
   file that binds pagination — grep for `PaginationQuery`,
   `PaginationMetadata`, `LoginRequest`, `LoginResponse`,
   `LoginUserResponse`, `CurrentAccountResponse`, `TenantDiscoveryResponse`,
   `DiscoveredTenantResponse`, `DashboardSummaryResponse` across
   `src/modules/iam/**` to find every reference before you start, and again
   after your edits to prove zero remain unresolved) to add
   `using TenantForge.Modules.Iam.Contract.Queries;` and/or
   `using TenantForge.Modules.Iam.Contract.Responses;` and/or
   `using TenantForge.Modules.Iam.Contract.Requests;` as needed.
5. Leave `AuthenticatedAccount` (`features/login/AuthenticatedAccount.cs`)
   and `PaginationSupport`'s binding/execution logic
   (`TryBind`/`PageAsync`/`Parse`) exactly where they are — they are
   explicitly excluded in the slice document.
6. Do not touch `features/users/**`, `features/tenants/**`,
   `features/tenantmembers/**`, `features/roles/**`,
   `features/invitations/**`, `features/audit/**` — those move in B022/B023.

# Acceptance

- The solution builds with the new project included.
- `TenantForge.Modules.Iam.csproj` has exactly one new `ProjectReference`
  entry (to the Contract project) in addition to its existing
  `TenantForge.BuildingBlocks` reference.
- The nine listed types exist only in
  `TenantForge.Modules.Iam.Contract`, under the documented namespace, as
  `public sealed record`; no duplicate definition remains in
  `TenantForge.Modules.Iam`.
- `grep -rn "PaginationQuery\|PaginationMetadata\|LoginRequest\|LoginResponse\|LoginUserResponse\|CurrentAccountResponse\|TenantDiscoveryResponse\|DiscoveredTenantResponse\|DashboardSummaryResponse" src/modules/iam/TenantForge.Modules.Iam/features/` shows only usages (via the new `using`), never a second declaration.
- Every existing IAM route's HTTP behavior (status codes, JSON field
  names/casing) is unchanged — proven by the full integration suite, not
  by inspection alone.
- `docs/modules/IAM.md` Section 4 (source-code map) gains one row for
  `src/modules/iam/TenantForge.Modules.Iam.Contract/`; Section 3
  (dependency and composition boundary) gains the updated dependency
  diagram from the slice document. State
  `IAM.md impact: updated — Section 3, Section 4` in self-review and the
  PR body.
- Review evidence separates the mechanical move (no logic change) from
  any incidental formatting; there should be no incidental formatting —
  keep the diff to exactly the described moves and `using` updates.

# Verification

Automated:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

The full suite must pass with zero new failures and zero skipped tests.
Do not narrow the filter — this task's entire proof of "no behavior
change" is the existing suite passing unmodified.

Manual:

- Sign in as the seeded platform administrator, confirm
  `POST /api/auth/login` still returns the same JSON shape.
- Call `GET /api/auth/me` and `GET /api/auth/me/tenants` (with a
  `pageNumber`/`pageSize` query) and confirm identical shapes, including
  pagination metadata field names.
- Call `GET /api/platform/dashboard-summary` as the platform admin and
  confirm the same shape.
- Restart the API against the same database and confirm migration/seeding
  is still a no-op (idempotent), proving the composition seam is intact.

# Lifecycle

Add row `B021` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B020`, and Spec link
`tasks/backend/B021-iam-contract-foundation.md`. The ledger is the only
source of truth for status/dependencies; do not duplicate those fields
here beyond what this section states for registration.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/023-iam-contract-separation.md` is the permanent
record and is never deleted.
