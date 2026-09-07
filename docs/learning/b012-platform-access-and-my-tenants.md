# B012 — Close platform user access and expose my tenants

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs`
  — `GET/POST /api/platform/users` are now admin-only. The old
  `IsPlatformAdminOrTenantPermissionAsync` helper (platform admin **or** a
  tenant permission `IAM.Users.View`/`IAM.Users.Create` resolved through a
  supplied `tenantId`) is deleted, and the `tenantId` query parameter is no
  longer bound at all. Both endpoints use the existing
  `PlatformAdmin` authorization policy, exactly like
  `/api/platform/tenants` and the dashboard.
- `src/modules/iam/TenantForge.Modules.Iam/features/account/TenantDiscoveryFeature.cs`
  — new feature mapping `GET /api/auth/me/tenants`, the S11 tenant-discovery
  endpoint consumed by F018's tenant switcher.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — one line:
  `MapTenantDiscoveryFeature()` added to the module's public mapping seam.
- `tests/integration/TenantForge.Api.IntegrationTests/RolePermissionIntegrationTests.cs`
  — the B009 test that expected a tenant "User Manager" role to reach the
  global Users API is **replaced in place** (not deleted) with
  `LegacyUserPermissionRole_CanNoLongerAccessPlatformUsers`, asserting the
  explicit S11 decision: the role still resolves its permissions, but both
  platform endpoints now answer 403.
- `tests/integration/TenantForge.Api.IntegrationTests/UserManagementIntegrationTests.cs`
  — new cases: Member **and** Owner denied with no / own / foreign `tenantId`,
  denied create persists nothing, denied read leaks no identity fields.
- `tests/integration/TenantForge.Api.IntegrationTests/TenantDiscoveryIntegrationTests.cs`
  — new: full discovery contract plus 401/403/fail-closed cases.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs`
  — the shared fixture now derives from an abstract `IamDbFixtureBase`; a
  dedicated `TenantDiscoveryDbFixture` gives the discovery tests their own
  PostgreSQL database (see section 5). `ApiFactory`'s fixture parameter was
  typed to the base class.
- `docs/design/s11-platform-tenant-boundaries.md` — the contract document for
  F018, including the documented supersession of the S09 example.
- `tasks/TASKS.md` — active row status only.

## 2. Request flow from endpoint to response

**`GET /api/platform/users` (admin-only)**
1. The JWT bearer handler validates the token; `RequireAuthorization(PlatformAdmin)`
   requires the `isPlatformAdmin == "true"` claim. Unauthenticated → 401
   (challenge); authenticated non-admin → 403 (forbid) **before the handler
   runs**.
2. The handler no longer reads `principal` or any `tenantId` — there is nothing
   left to authorize manually, and a client-supplied tenant can no longer even
   be parsed.
3. The unchanged query lists the first 50 accounts and returns the B006
   payload.

**`GET /api/auth/me/tenants` (new)**
1. `.RequireAuthorization()` → missing/invalid JWT is 401.
2. The `sub` claim is parsed to a GUID; an unparseable/empty subject is 403.
3. The account row is verified: it must exist and be `Active`. A missing or
   disabled account is 403 — a stateless JWT cannot outlive row state.
4. `TenantMemberships` for that account are joined to `Tenants` filtered to
   `Status == Active`; the projection carries `id/name/slug/status/role`.
5. Rows are ordered by tenant name then id and mapped to the response record;
   no memberships simply produces an empty array.

## 3. Backend concepts introduced

- **Policy-based authorization instead of in-handler checks.** The Users
  feature previously re-derived admin/permission status inside the handler and
  returned `Results.Forbid()` itself. Moving the decision to a named claim
  policy keeps the 401-vs-403 distinction in the authorization middleware and
  makes "who may call this" visible at the route mapping.
- **Fail-closed verification of the identity row.** Authentication proves the
  token is ours; it does not prove the account is still usable. The discovery
  endpoint re-checks the account row, so disabled/removed accounts get 403
  even with a still-signature-valid token.
- **Discovery as a projection, not a bypass.** The endpoint answers only
  "which tenants do *I* belong to" by projecting the caller's own membership
  rows. A platform admin goes through the identical code path — the admin
  claim never widens the query.
- **Ordering and emptiness as contract.** Stable ordering (name, then id) and
  an explicit `[]` for "no memberships" are part of the wire contract F018
  relies on.

## 4. Important security decisions

- **Default deny, server-side.** Platform user listing/creation requires the
  admin claim; tenant membership, tenant permissions, or a `tenantId` query
  value grant nothing. The `tenantId` parameter was removed from the binding
  entirely so the "supplied tenant" path cannot be revived by accident.
- **Denied responses leak nothing.** The 403 from the users endpoints is an
  empty body; integration tests assert the body contains no user/email/
  displayName data, and a denied create is asserted to persist no account.
- **Tenant status is enforced in the query, not the UI.** Suspended tenants are
  filtered server-side; they are excluded rather than returned with a status.
- **No secrets, no stateless trust.** No tokens, hashes or passwords appear in
  responses or logs; identity fields come only from validated claims and
  verified rows.
- **The S09 example was superseded deliberately and documented**
  (`docs/design/s11-platform-tenant-boundaries.md`) before the code changed;
  the corresponding B009 test was updated to the new decision, not removed.

## 5. Alternatives deliberately postponed

- **No tenant-scoped user management.** Tenant provisioning of platform
  accounts is not a product need; closing the boundary is the feature.
- **No pagination on the discovery endpoint** (and none added to the users
  list) — consistent first-page behavior is B015's scope.
- **No membership soft-delete / "deleted membership" state** — the schema has
  no deleted-membership row, so "exclude deleted memberships" reduces to
  "row absent".
- **No changes to `/api/tenants/{tenantId}/members`, roles or the permission
  catalog** — those belong to B013.

## 6. Commands and manual steps to verify

```text
dotnet.exe build src/api/TenantForge.Api/TenantForge.Api.csproj
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
```

Focused runs:

```text
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter "FullyQualifiedName~TenantDiscoveryIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter "FullyQualifiedName~UserManagementIntegrationTests"
```

Manual demo (real API, Development, port 5000; from WSL curl through the
gateway IP `172.30.176.1`):

1. Log in as the seeded admin → `GET /api/platform/users` returns 200 with the
   user list; `POST` creates an ordinary account (201).
2. Log in as that ordinary account → `GET`/`POST /api/platform/users` returns
   403 with no tenantId, with its own tenantId, and with a foreign tenantId;
   the response body is empty.
3. As that account, `GET /api/auth/me/tenants` returns exactly its active
   memberships ordered by name; a suspended tenant is absent; an account with
   no memberships gets `{"tenants":[]}`; an invalid or missing JWT gets 401.

## 7. Review questions

1. Why is binding `tenantId` removed from the platform users routes instead of
   bound and ignored? What class of regression does that prevent?
2. The discovery endpoint verifies the account row even though the JWT already
   validated. Which real-world state change would that catch, and what would a
   client observe before and after it happens?
3. The shared test database made the discovery tests able to break an
   unrelated user-management test through the 50-row page of
   `GET /api/platform/users`. What makes that interaction possible, and why is
   an isolated database the right fix rather than changing the page size or
   the other test?
