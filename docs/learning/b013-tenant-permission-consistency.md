# B013 — Tenant permission consistency

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs`
  centralizes tenant authorization for roles, resolved permissions, invitations
  and audit. It replaces the old S09 catalog keys with the four S12 tenant
  permissions, applies the same active-account/active-tenant/active-membership
  checks everywhere, and protects the final role administrator.
- `src/modules/iam/TenantForge.Modules.Iam/features/invitations/InvitationsFeature.cs`
  uses `RolesFeature.AuthorizeTenantAccessAsync` instead of its local helper.
  Invitation list now requires `IAM.Invitations.View`; create requires
  `IAM.Invitations.Create`.
- `src/modules/iam/TenantForge.Modules.Iam/features/audit/AuditFeature.cs`
  uses the same tenant authorization helper and requires `IAM.Audit.View`.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/20260912183000_NormalizeTenantPermissionKeys.cs`
  migrates existing persisted role keys from S09/S10 to S12.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/20260912183000_NormalizeTenantPermissionKeys.Designer.cs`
  carries the EF migration metadata so the data migration is discovered and
  applied.
- `tests/integration/TenantForge.Api.IntegrationTests/RolePermissionIntegrationTests.cs`
  updates role/permission behavior tests to S12 and adds edge cases for active
  account/tenant/membership checks and last-administrator protection.
- `tests/integration/TenantForge.Api.IntegrationTests/InvitationAuditIntegrationTests.cs`
  updates invitation/audit tests to use S12 permission keys and adds
  per-permission allow/deny coverage.
- `tests/integration/TenantForge.Api.IntegrationTests/PermissionKeyMigrationTests.cs`
  verifies the data migration against representative pre-S12 data in an
  isolated PostgreSQL container.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs`
  adds an isolated collection for role-permission tests so their many test
  accounts do not pollute the shared database used by unrelated first-page
  user-list tests.
- `docs/design/s12-permission-consistency.md` records the server contract that
  F019 will consume.
- `tasks/TASKS.md` tracks B013 through the lifecycle.

## 2. Request flow from endpoint to response

### Catalog and resolved permissions

1. `GET /api/permissions/catalog` returns the unchanged catalog envelope but
   now only the four supported tenant keys:
   `IAM.Roles.Manage`, `IAM.Invitations.View`,
   `IAM.Invitations.Create`, `IAM.Audit.View`.
2. `GET /api/tenants/{tenantId}/me/permissions` calls the shared tenant
   authorization helper.
3. The helper validates the authenticated subject, active account row, active
   tenant row and tenant membership row.
4. If the membership role is `Owner`, all four keys resolve. Otherwise the
   response is the union of assigned custom-role keys filtered to known S12 keys.

### Role writes

1. Create/update/assign/unassign parse the tenant and subject.
2. Shared authorization requires `IAM.Roles.Manage`, which an Owner receives
   through resolution.
3. Role request bodies are validated against the four-key set. Old keys such as
   `IAM.Users.View` and unknown keys return `400` with `permissionKeys` errors.
4. Mutating operations run inside a transaction and take a tenant-scoped
   PostgreSQL advisory lock.
5. Before a role update or unassignment commits, the server evaluates the
   proposed final state and rejects the mutation with `409` if no effective role
   administrator would remain.
6. Successful role changes still write the existing audit events.

### Invitations and audit

1. Invitation/audit endpoints also call the shared tenant authorization helper.
2. Invitation list requires `IAM.Invitations.View`; invitation create requires
   `IAM.Invitations.Create`; audit read requires `IAM.Audit.View`.
3. A member with only one of these keys receives only that capability. For
   example, an invitation viewer can list invitations but cannot create
   invitations or read audit.

## 3. Backend concepts introduced

- **One tenant authorization decision:** the code now has one place that answers
  whether a request has an active account, active tenant and current membership.
  Features still stay direct and readable, but they no longer duplicate subtly
  different membership checks.
- **Catalog keys as an enforced contract:** persisted role keys and new request
  validation both use the same four supported permissions, so the UI catalog,
  resolved permissions and server decisions match.
- **Final-state authorization invariant:** last-administrator protection is not
  based on the single row being touched. It evaluates the tenant after the
  proposed change and counts distinct active accounts.
- **Tenant-scoped concurrency guard:** role mutations take a PostgreSQL advisory
  lock derived from the tenant id so two concurrent demotions cannot both pass
  a stale last-admin check.
- **Data-only EF migration:** no model shape changed, but persisted arrays still
  need migration. The migration class contains SQL and a small designer metadata
  file so EF discovers it.

## 4. Important security decisions

- **Default deny:** parse failures, missing accounts, disabled accounts,
  suspended tenants and removed memberships return `403` with no tenant data.
- **Platform admin is not a tenant bypass:** tenant APIs still require tenant
  membership and tenant permissions. Platform user access remains admin-only
  from B012.
- **Owners are authoritative:** membership `Owner` grants all four tenant
  permissions, independent of custom role rows.
- **No accidental invitation/audit grants:** the migration maps only
  `IAM.Tenants.Create` to `IAM.Roles.Manage`; it removes obsolete keys and does
  not manufacture invitation/audit permissions for ordinary roles.
- **Distinct active accounts protect administration:** two admin-role assignments
  to the same person count once; disabled accounts do not count.

## 5. Alternatives deliberately postponed

- No new endpoint was added; F019 consumes the existing catalog and resolved
  permissions endpoints.
- No generalized authorization framework, caching, deny rules or per-user grants
  were introduced.
- No tenant-scoped user provisioning was added; B012 closed platform users to
  platform admins only.
- No invitation acceptance, email delivery or pagination work was included.
- No frontend code was changed; F019 owns catalog consumption in the UI.

## 6. Commands and manual steps to verify

Commands:

```text
dotnet.exe build tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~RolePermissionIntegrationTests|FullyQualifiedName~InvitationAuditIntegrationTests|FullyQualifiedName~PermissionKeyMigrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build
dotnet.exe build TenantForge.sln
```

Manual demo steps used:

1. Start the real API in Development on port 5000 and curl through the WSL
   gateway IP.
2. Log in as the platform admin and call `GET /api/permissions/catalog`; verify
   the four S12 keys only.
3. Create demo owner/member accounts and a demo tenant through the existing
   admin endpoints.
4. As the tenant Owner, call resolved permissions and verify all four keys.
5. As the Owner, create an `Invitation Viewer` role and assign it to the member.
6. As the member, verify resolved permissions contain only
   `IAM.Invitations.View`.
7. As the member, verify invitation list succeeds, invitation create and audit
   read return `403`, and `/api/platform/users?tenantId=...` still returns `403`.

## 7. Review questions

1. Why does S12 replace `IAM.Tenants.Create` with `IAM.Roles.Manage` instead of
   continuing to treat tenant creation as an ownership proxy?
2. When a role with `IAM.Roles.Manage` is assigned twice to the same account,
   why does that still count as only one effective role administrator?
3. What could go wrong if the last-administrator check and the role mutation did
   not run under the same tenant-scoped transaction/lock?
