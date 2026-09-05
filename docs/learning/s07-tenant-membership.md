# B007 — S07 tenant membership API: learning note

## Files changed and why

### New

- `src/modules/iam/TenantForge.Modules.Iam/domain/Tenant.cs` — the tenant aggregate root for this slice. It owns tenant identity, display name, normalized slug, status and timestamps.
- `src/modules/iam/TenantForge.Modules.Iam/domain/TenantStatus.cs` — the small tenant lifecycle contract (`Active`, `Suspended`) used by the API response.
- `src/modules/iam/TenantForge.Modules.Iam/domain/TenantMembership.cs` — the membership row that links one account to one tenant.
- `src/modules/iam/TenantForge.Modules.Iam/domain/TenantMembershipRole.cs` — starts tenant roles with the only role B007 needs: `Owner`.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TenantMap.cs` — EF Core mapping for `iam_tenants`, including the unique normalized-slug index.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TenantMembershipMap.cs` — EF Core mapping for `iam_tenant_memberships`, including tenant/account uniqueness and foreign keys.
- `src/modules/iam/TenantForge.Modules.Iam/features/tenants/TenantsFeature.cs` — the B007 vertical slice. It maps platform tenant list/create endpoints, validates requests, creates the tenant plus first Owner membership atomically and maps safe tenant summaries.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/*AddTenantMemberships*` — the database migration that creates tenant and membership tables.
- `tests/integration/TenantForge.Api.IntegrationTests/TenantMembershipIntegrationTests.cs` — API-level coverage for create/list, authorization, invalid owner, duplicate slug and no-partial-data behavior.

### Modified

- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/IamDbContext.cs` — adds `Tenants` and `TenantMemberships` sets and applies the new mappings.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — maps the tenant feature through the IAM module seam. The API host still composes IAM through `IamModule` only.
- `tasks/TASKS.md` — B007 moved through the task lifecycle for this slice.

## Request flow from endpoint to response

### `GET /api/platform/tenants`

1. The browser calls `GET /api/platform/tenants` with a Bearer token.
2. ASP.NET Core authenticates the JWT and runs the existing `PlatformAdmin` authorization policy.
3. The endpoint queries `IamDbContext.Tenants` and counts memberships from `IamDbContext.TenantMemberships`.
4. Database rows are materialized first, then mapped to the stable frontend contract: `id`, `name`, `slug`, `status`, `memberCount` and `createdAtUtc`.
5. The response is `200 OK` with `{ tenants: [...] }`.

### `POST /api/platform/tenants`

1. The browser calls `POST /api/platform/tenants` with `name`, `slug` and `ownerUserId`.
2. Authentication and `PlatformAdmin` authorization run before endpoint logic.
3. The endpoint validates the request server-side:
   - name is required and at most 80 characters;
   - slug is normalized and must be 3–50 characters using letters, digits and dashes;
   - ownerUserId is required and must be a non-empty GUID.
4. The endpoint checks the owner account exists and is active.
5. A duplicate normalized slug returns a stable `409 Conflict` problem before any insert when possible.
6. The endpoint starts a database transaction, adds the `Tenant` and its first `Owner` `TenantMembership`, saves both and commits.
7. If the unique index catches a duplicate slug race, the transaction rolls back and the API still returns the stable `409 Conflict` problem.
8. The response is `201 Created` with the tenant summary and `memberCount: 1`.

## Backend concepts introduced

- **Aggregate boundary.** `Tenant` is the root being created. The first `TenantMembership` is part of the creation invariant because this slice requires every new tenant to start with exactly one Owner.
- **Atomic write.** The tenant and membership are saved inside one transaction. Either both rows exist, or neither does.
- **Membership identity.** A user is not “inside” a tenant because the browser selected one. The durable fact is the membership row linking `TenantId` and `AccountId`.
- **Normalized slug uniqueness.** Slugs are normalized on the server and protected by a database unique index, so case and formatting differences do not create duplicate tenants.
- **Safe response mapping.** The API returns summaries only. It does not expose owner account details or membership internals in this slice.

## Important security decisions

- **Platform-admin protection.** Both endpoints require the existing `PlatformAdmin` policy. Missing tokens get `401`; valid non-admin tokens get `403`.
- **Server-side validation is authoritative.** The browser form validates for usability, but the API repeats validation because direct HTTP callers can bypass the UI.
- **Selection is not authorization.** B007 lists and creates platform tenants; it does not treat a selected tenant slug as proof of access. Tenant-scoped authorization is deliberately enforced in B008.
- **Default no partial data.** Missing, invalid or unknown owners are rejected before the transaction. Duplicate slug races roll back the transaction.
- **Stable conflict errors.** Duplicate slug responses do not leak EF Core exception details or database index names.

## Alternatives deliberately postponed

- **Tenant isolation for tenant-scoped data.** B008 proves missing/cross-tenant context denial with a tenant-scoped query path.
- **Editing, suspending or deleting tenants.** B007 only creates and lists tenants for the F011 switcher.
- **Adding/removing members after creation.** This slice creates exactly the first Owner; membership management comes later.
- **Custom tenant roles and permissions.** Only `Owner` exists now because B009 introduces the permission matrix.
- **Invitations and audit events.** Those are S10 concerns, not needed for the current browser demo.
- **Repository/service abstraction.** The vertical slice uses `IamDbContext` directly to keep the request path readable.

## Commands and manual steps to verify

Automated checks used for this slice:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests --filter "FullyQualifiedName~TenantMembershipIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
dotnet.exe build TenantForge.sln
```

Manual API smoke path for the browser demo readiness:

1. Start PostgreSQL with `docker compose up -d` if it is not already running.
2. Start the API from this backend clone using the Windows SDK via WSL interop:

   ```bash
   dotnet.exe run --project ./src/api/TenantForge.Api/ --urls "http://0.0.0.0:5100"
   ```

3. From WSL, call through the gateway IP from `ip route`:

   ```bash
   GW=<gateway from ip route>
   TOKEN=$(curl -s -X POST "http://$GW:5100/api/auth/login" \
     -H 'Content-Type: application/json' \
     -d '{"email":"admin@tenantforge.local","password":"local-development-password"}' \
     | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')

   OWNER_ID=$(curl -s "http://$GW:5100/api/platform/users" \
     -H "Authorization: Bearer $TOKEN" \
     | python3 -c 'import json,sys; print(json.load(sys.stdin)["users"][0]["id"])')

   curl -i -X POST "http://$GW:5100/api/platform/tenants" \
     -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' \
     -d "{\"name\":\"Acme\",\"slug\":\"acme\",\"ownerUserId\":\"$OWNER_ID\"}"

   curl -i "http://$GW:5100/api/platform/tenants" \
     -H "Authorization: Bearer $TOKEN"
   ```

Expected results: tenant creation returns `201 Created`, the list returns `200 OK`, and the new tenant appears with `memberCount: 1`.

## Review questions

1. Why does B007 create the tenant and first Owner membership in the same transaction instead of two independent saves?
2. Why does selecting `/t/:slug` in the browser not prove that the current user is authorized for that tenant?
3. Why does the endpoint check for duplicate slugs before saving and still rely on a database unique index?
