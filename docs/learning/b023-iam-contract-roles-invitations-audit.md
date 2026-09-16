# B023 — IAM Contract roles/invitations/audit types + the exported-surface lock

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam.Contract/Requests/CreateRoleRequest.cs`,
  `Requests/UpdateRoleRequest.cs`, `Requests/CreateInvitationRequest.cs`,
  `Responses/PermissionCatalogResponse.cs`, `Responses/PermissionGroupResponse.cs`,
  `Responses/PermissionResponse.cs`, `Responses/TenantRolesResponse.cs`,
  `Responses/PagedTenantRolesResponse.cs`, `Responses/TenantRoleResponse.cs`,
  `Responses/ResolvedPermissionsResponse.cs`, `Responses/InvitationListResponse.cs`,
  `Responses/InvitationResponse.cs`, `Responses/AuditListResponse.cs`,
  `Responses/AuditEventResponse.cs` (new) — the final fourteen S23 contract
  records, each a `public sealed record` with the exact same members as before.
  This was a fully mechanical batch: unlike B022's `UserResponse`, none of the
  fourteen carried a factory that references an internal domain type, so every
  declaration moved whole.
- `features/roles/RolesFeature.cs` — deleted its nine contract records
  (lines 418–426 before the move); the four handler-only records
  (`TenantAccess`, `ActorSnapshot`, `AssignmentValidation`, `RemovedAssignment`)
  stay in the file, with a short `B023/S23` comment marking the boundary. The
  `CatalogGroups` static catalog *data* also stays — it is business data that
  now merely constructs contract records via the existing
  `using ...Contract.Responses;`. Added `using ...Contract.Requests;`.
- `features/invitations/InvitationsFeature.cs` — deleted its three records;
  added `using ...Contract.Requests;` (the `Responses` using was already
  present from B021).
- `features/audit/AuditFeature.cs` — deleted its two records; no `using`
  change needed (it already imported `Contract.Responses`).
- `tests/integration/TenantForge.Api.IntegrationTests/IamContractArchitectureTests.cs`
  (new) — the boundary lock for the whole 32-type project, added in the batch
  that completes it (per the slice). Four `[Fact]`s, mirroring
  `BuildingBlocksArchitectureTests`' techniques verbatim where the Spec asked:
  - `IamContract_ProjectHasZeroProjectReferences` — parses the Contract `.csproj`
    with `XDocument` and asserts no `ProjectReference` descendants.
  - `IamContract_AssemblyDoesNotReferenceApiModulesEfOrNpgsql` —
    `GetReferencedAssemblies()` on the compiled Contract assembly (reached via
    `typeof(LoginRequest).Assembly`) must contain no name starting with
    `TenantForge.Modules.`, `TenantForge.Api`, `Microsoft.AspNetCore.`,
    `Microsoft.EntityFrameworkCore` or `Npgsql`.
  - `IamModule_ReferencesContractExactlyOnce` — the IAM `.csproj` has exactly
    one `ProjectReference` pointing at the Contract csproj (its other reference,
    BuildingBlocks, is excluded by the filter).
  - `IamContract_ExportsOnlyTheApprovedProductionTypes` — the non-compiler-
    generated exported types, sorted ordinally, equal the hand-enumerated
    32-entry roster (6 Requests + 1 Query + 25 Responses).
- `docs/modules/IAM.md` — Section 4 source-code map (the Roles/permissions,
  Invitations and Audit rows now name the Contract project as the records'
  home; the roles row records that the four handler-only records stay) and
  Section 14 test map (new `IamContractArchitectureTests.cs` row).

No `.csproj` or `TenantForge.sln` change: the Contract project already existed
(B021), the single IAM→Contract `ProjectReference` was reused (B021), and the
test project needs no new reference — Contract types reach the test
transitively through `Api → IAM → Contract`.

## 2. Request flow from endpoint to response

`POST /api/tenants/{tenantId}/roles` → `RolesFeature` binds the contract-owned
`CreateRoleRequest` → `AuthorizeTenantAccessAsync` (module-internal, unchanged)
gates on `IAM.Roles.Manage` → EF insert + audit event in one transaction →
`BuildRoleResponsesAsync` (module-internal) builds `Contract.Responses`
`TenantRoleResponse` rows → `Results.Created`. `GET /api/permissions/catalog`
returns `Results.Ok(new PermissionCatalogResponse(CatalogGroups))` where
`CatalogGroups` (the Persian-labelled business data) is a module field whose
element type is now the contract record. `POST /api/tenants/{tenantId}/
invitations` binds the contract `CreateInvitationRequest`, takes the advisory
lock, persists the hashed token, and returns the contract `InvitationResponse`.
`GET /api/tenants/{tenantId}/audit` maps rows to the contract
`AuditEventResponse`. In every case only the type's home assembly changed.

## 3. Backend concepts introduced

- **Locking a boundary, not just moving code.** B021/B022 relocated types; B023
  makes the relocation *durable*. A module's public surface is normally
  accidental — whatever happens to be `public`. The architecture test turns it
  into a checked invariant: any future type added to the Contract project
  fails the exact-match assertion until someone deliberately edits the roster,
  which is the review moment the slice wants.
- **Two levels of reference checking.** The `.csproj` check (via `XDocument`)
  guards the *source* dependency graph — what a future engineer wires up. The
  `GetReferencedAssemblies()` check guards the *compiled* dependency graph —
  what the runtime actually loaded, catching a `PackageReference` (e.g. EF
  Core) that a `.csproj`-only check would miss. Both are needed because they
  fail on different mistakes.
- **Hand-enumerated rosters.** The 32 fully-qualified names are copied into the
  test, never computed (e.g. "assert count == GetExportedTypes().Count" would
  pass no matter what leaked in). The roster is also ordered by the same
  `StringComparer.Ordinal` the assertion uses — note the Spec lists it in
  folder-convention order, but ordinally `Queries` sorts before `Requests`
  (`Q` < `R`), so the test array puts `PaginationQuery` first.
- **Transitive project references.** The test project references only the API
  host, yet names `Contract.Requests` types — C# project references are
  transitive, so the test compiles. This is exactly how
  `BuildingBlocksArchitectureTests` reaches `IModuleConfig` without a direct
  BuildingBlocks reference.

## 4. Security decisions

- No authentication, authorization, configuration or fail-closed behavior
  changed. The `PlatformAdmin` policy, `AuthorizeTenantAccessAsync`, the
  last-effective-administrator protection and the 401/403 non-disclosure rule
  are untouched — proven by the 116-test suite.
- The visibility change (`internal sealed` → `public sealed`) is the same
  boundary re-drawing as B021/B022: these types were always serialized to
  callers; they now have an explicit, reviewable, *test-locked* public surface.
  The four handler-only records deliberately remain `internal`.
- No secrets, credentials or tokens are logged; nothing new is logged. The
  invitation raw-token flow (generate → hash → persist only the hash) is
  unchanged.

## 5. Alternatives deliberately postponed

- **A `module-contract-project`-level generic test helper.** The new test
  duplicates the small `ProjectReferences`/`IsCompilerGenerated`/`Locate-
  RepositoryRoot` helpers rather than extracting a shared test utility —
  TenantForge has exactly two such tests today; a shared helper is
  BuildingBlocks/skill territory, not this slice.
- **Locking the *four* handler-only records' presence in `RolesFeature.cs`.**
  They are `internal` and module-private; the exported-surface test can only
  see the Contract assembly, and a test asserting on another assembly's
  internals would couple the test to implementation layout. Their placement is
  instead documented in `IAM.md` Section 4.
- **The living Contract handbook (`docs/contracts/iam.md`).** Deliberately
  B024, once the 32-type roster is `done` — the same ordering B020 used after
  B018.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
# Expected: build succeeded; Passed: 116, Failed: 0, Skipped: 0
# Focused first check:
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~IamContractArchitectureTests|FullyQualifiedName~RolePermissionIntegrationTests|FullyQualifiedName~InvitationAuditIntegrationTests"
```

Grep proof (declarations only in the Contract project — exactly 14; the four
handler-only records still in `RolesFeature.cs`):

```bash
grep -rnE 'record (CreateRoleRequest|UpdateRoleRequest|PermissionCatalogResponse|PermissionGroupResponse|PermissionResponse|TenantRolesResponse|PagedTenantRolesResponse|TenantRoleResponse|ResolvedPermissionsResponse|CreateInvitationRequest|InvitationListResponse|InvitationResponse|AuditListResponse|AuditEventResponse)\b' src/modules/iam/
```

Manual demo (API on `http://0.0.0.0:5000`, curl through the WSL gateway IP,
seeded admin as the owner of a fresh tenant):

1. `GET /api/permissions/catalog` → `200` `{groups[{id,label,description,
   permissions[{key,label,description,kind}]}]}`.
2. `GET /api/tenants/{tenantId}/roles` → `200` `{roles[], pagination}`.
3. `POST /api/tenants/{tenantId}/roles` `{name, permissionKeys:[
   "IAM.Audit.View"]}` → `201` + the eight-field `TenantRoleResponse`
   (`kind:"custom"`, `memberIds:[]`).
4. `GET /api/tenants/{tenantId}/members` → capture the owner's membership `id`.
5. `PUT /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}` → `200`
   `{roles[]}` (member's id appears in `memberIds`); then `DELETE` the same →
   `200` (idempotent, `memberIds` back to `[]`).
6. `GET /api/tenants/{tenantId}/me/permissions` → `200` `{permissions[]}` —
   the owner union, all four catalog keys.
7. `POST /api/tenants/{tenantId}/invitations` `{email, role:"Viewer"}` → `201`
   + six-field `InvitationResponse`; `GET /api/tenants/{tenantId}/invitations`
   → `200` `{invitations[], pagination}`.
8. `GET /api/tenants/{tenantId}/audit` → `200` `{events[{id, actor,
   actorEmail, action, target, details, createdAtUtc}], pagination}` showing
   `Role.Created`, `Role.Assigned`, `Role.Unassigned`, `Invitation.Created`.
9. Stop and restart the API against the same database → "No migrations were
   applied" + "seeding already-present" (idempotent).

## 7. Three review questions

1. The architecture test asserts the exported roster with `Assert.Equal`
   against a hand-written 32-entry array sorted ordinally. What is the
   concrete failure mode of the alternative — "assert the set has 32 types and
   each matches an expected prefix" — and why does exact ordinal equality make
   an *unintended* addition a build-blocking, visible diff instead of a silent
   growth?
2. The `.csproj` check and the `GetReferencedAssemblies()` check both guard
   the "Contract references nothing" rule. Construct a realistic change that
   would pass one but fail the other (hint: think about how a dependency can
   enter the compiled assembly without a new `<ProjectReference>`), and decide
   which failure you would rather catch first.
3. `RolesFeature.cs` still declares four `internal sealed record`s
   (`TenantAccess`, `ActorSnapshot`, `AssignmentValidation`, `RemovedAssignment`)
   right where the nine contract records used to sit. Why can't those four be
   part of the locked 32-type roster, and what would have to be true about one
   of them for it to ever earn a place in `TenantForge.Modules.Iam.Contract`?
