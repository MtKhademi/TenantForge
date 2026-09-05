# B008 — S08 tenant isolation API: learning note

## Files changed and why

### New

- `src/modules/iam/TenantForge.Modules.Iam/features/tenantmembers/TenantMembersFeature.cs` — the S08 tenant-scoped query slice. It maps `GET /api/tenants/{tenantId}/members`, resolves tenant context from the route, authorizes by the authenticated account's tenant membership and returns only members for that tenant.
- `tests/integration/TenantForge.Api.IntegrationTests/TenantIsolationIntegrationTests.cs` — adversarial API tests for valid tenant reads, cross-tenant route tampering, missing/invalid tenant context, missing authentication and platform-admin-without-membership denial.

### Modified

- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — maps the tenant-member feature through the public IAM module seam. The API host still composes IAM only through `IamModule`.
- `tasks/TASKS.md` — B008 moved through the task lifecycle for this slice.

## Request flow from endpoint to response

### `GET /api/tenants/{tenantId}/members`

1. The browser calls `GET /api/tenants/{tenantId}/members` with a Bearer token.
2. ASP.NET Core validates the JWT before the endpoint runs. Missing or invalid authentication receives `401` from authorization middleware.
3. The endpoint reads the `sub` claim from the validated principal and parses it as the authenticated account id.
4. The endpoint parses `{tenantId}` from the route. Missing, empty or malformed tenant context fails closed with `403`.
5. The endpoint checks `IamDbContext.TenantMemberships` for a row linking the authenticated account to the requested tenant.
6. If that membership is missing, the endpoint returns `403` without revealing whether the tenant exists or who belongs to it.
7. Once membership is proven, the endpoint verifies the tenant is active and queries memberships for **only that tenant id**.
8. The endpoint joins accounts for display fields and returns `{ tenant, members }`.

## Backend concepts introduced

- **Tenant context is explicit input, not trust.** The route tenant id tells the API which tenant the client wants. It does not prove access.
- **Membership authorization.** Access is granted by a durable membership row linking the authenticated account id to the requested tenant id.
- **Tenant-scoped query filtering.** The member query includes `membership.TenantId == tenantGuid`, so a valid user cannot accidentally read every tenant's members.
- **Horizontal access control tests.** The tests do not only check happy paths. They intentionally change the tenant id in the URL to prove another tenant's data remains denied.
- **Non-leaking forbidden responses.** Unknown tenant and real-but-forbidden tenant both return `403`, so the API does not become a tenant-enumeration endpoint.

## Important security decisions

- **Default deny.** If the account id claim is missing, the route id is invalid, the tenant is unknown or membership is missing, the endpoint denies access.
- **Platform admin is not a tenant-member bypass.** B008 does not define platform support access, so `isPlatformAdmin=true` alone is not enough to read tenant members.
- **Server-side isolation.** The API checks membership regardless of which route the browser shows or hides. A user manually editing the URL still gets `403`.
- **No hidden data in denial.** Forbidden responses do not include tenant names, member emails or membership details.
- **No new secrets.** The endpoint logs nothing and does not handle credentials or tokens directly.

## Alternatives deliberately postponed

- **Roles beyond Owner.** B008 uses membership existence only. Role-specific permissions are introduced in B009.
- **Platform support override.** The source allowed support access only if explicitly defined by the task. This task did not define it, so the safer choice is no override.
- **Tenant edit/suspension workflows.** The endpoint only checks active tenants and reads members.
- **Row-level security.** PostgreSQL RLS is not required for the first milestone; isolation is enforced in the application query and covered by integration tests.
- **Permission cache.** There is no permission matrix yet, so caching would be premature.
- **Tenant-specific business modules.** The slice proves IAM member isolation only.

## Commands and manual steps to verify

Automated checks used for this slice:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests --filter "FullyQualifiedName~TenantIsolationIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
dotnet.exe build TenantForge.sln
```

Manual API smoke path for the F012 browser demo readiness:

1. Start PostgreSQL with `docker compose up -d` if needed.
2. Start the API from this backend clone:

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

   curl -i "http://$GW:5100/api/platform/tenants" \
     -H "Authorization: Bearer $TOKEN"

   curl -i "http://$GW:5100/api/tenants/<tenant-id>/members" \
     -H "Authorization: Bearer $TOKEN"
   ```

Expected results: a token for an account that belongs to `<tenant-id>` returns `200 OK` with that tenant's members. A token for an account without that membership returns `403 Forbidden`.

## Review questions

1. Why does the endpoint require both a valid route tenant id and a membership row before returning members?
2. Why is `isPlatformAdmin=true` not treated as a tenant-member bypass in this slice?
3. What bug would the cross-tenant tampering test catch if the query forgot `membership.TenantId == tenantGuid`?
