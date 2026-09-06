# B009 — Role permission API

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/domain/*Role*` adds tenant custom roles and member-role assignments.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/*Role*` maps those tables and adds the migration that stores tenant roles, permission keys and assignments.
- `src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs` implements the S09 permission catalog, tenant role CRUD, member assignment and resolved-permissions endpoints.
- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs` keeps platform-admin access and adds tenant permission checks for one read and one write operation.
- `tests/integration/TenantForge.Api.IntegrationTests/RolePermissionIntegrationTests.cs` covers allowed, denied, cross-tenant, validation and last-effective-Owner behavior.
- `tasks/TASKS.md` tracks the B009 lifecycle.

## 2. Request flow from endpoint to response

1. The browser sends an authenticated request with a JWT.
2. `.RequireAuthorization()` rejects missing or invalid tokens before feature code trusts any identity.
3. Role endpoints parse the tenant id and authenticated account id from the validated principal.
4. Reads require active membership in that tenant.
5. Role creation, update and assignment require an effective Owner for the tenant.
6. Permission keys are checked against the small S09 catalog before persistence.
7. Role assignment verifies that the target member and role both belong to the selected tenant.
8. `GET /api/tenants/{tenantId}/me/permissions` resolves built-in Owner permissions plus assigned custom-role permissions for the current member.
9. Existing user-list/user-create endpoints check tenant permissions when the caller is not a platform admin.

## 3. Backend concepts introduced

- Permission catalog: a fixed list of stable keys the UI can render and the API can validate.
- Tenant custom role: a tenant-owned named bundle of permission keys.
- Role assignment: a join between tenant membership and tenant role.
- Resolved permissions: the effective set the current account has in one tenant.
- Server-side authorization: navigation may hide controls, but API endpoints still enforce permissions.

## 4. Important security decisions

- Tenant role data is tenant-scoped; role names are unique only inside one tenant.
- Non-members receive `403` without learning role/member details from another tenant.
- Non-Owner members cannot manage roles directly through HTTP.
- Member and role ids return `404` only after the caller is authorized for the tenant.
- Unknown permission keys are rejected; wildcard or caller-supplied arbitrary permissions are not accepted.
- The API protects the last effective Owner from being removed by assignment changes.
- Platform admins keep existing access, while tenant permissions allow demonstrated non-admin read/write behavior.

## 5. Alternatives deliberately postponed

- Direct per-user allow/deny overrides.
- Wildcard permissions.
- Platform-wide custom roles.
- Permission caching and distributed invalidation.
- Audit trail and invitations.
- Impersonation.

## 6. Commands and manual steps to verify

Commands:

```text
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter RolePermissionIntegrationTests
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
dotnet.exe build
```

Manual browser handoff for F014:

1. Run the API with migrated IAM tables.
2. Sign in as a tenant Owner.
3. Open Roles for a tenant.
4. Create `User Manager` with `IAM.Users.View` and `IAM.Users.Create`.
5. Assign it to a member in the same tenant.
6. Sign in as that member and confirm resolved permissions include those keys.
7. Confirm granted user read/write behavior works through the API.
8. Try an ungranted direct API call and confirm it returns `403`.

## 7. Review questions

1. Why does the API validate permission keys against a server catalog instead of trusting the UI list?
2. Why do role assignment endpoints check tenant membership and ownership before returning `404` for ids?
3. What is the difference between hiding navigation using resolved permissions and enforcing authorization on the API endpoint?
