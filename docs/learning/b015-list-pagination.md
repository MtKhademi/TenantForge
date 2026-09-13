# B015 — Consistent server pagination

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs`
  adds the IAM-local pagination primitives used by all seven collection reads:
  query validation, metadata calculation, count-before-page, and offset overflow
  protection.
- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs`
  pages `GET /api/platform/users` after the existing platform-admin policy.
- `src/modules/iam/TenantForge.Modules.Iam/features/tenants/TenantsFeature.cs`
  pages `GET /api/platform/tenants` while preserving the member-count payload.
- `src/modules/iam/TenantForge.Modules.Iam/features/account/TenantDiscoveryFeature.cs`
  pages `GET /api/auth/me/tenants` after filtering to the caller's active
  memberships.
- `src/modules/iam/TenantForge.Modules.Iam/features/tenantmembers/TenantMembersFeature.cs`
  pages tenant members while keeping the sibling `tenant` context in the
  response.
- `src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs`
  pages role reads with a new paged response type but keeps full role snapshots
  for mutation responses and security logic.
- `src/modules/iam/TenantForge.Modules.Iam/features/invitations/InvitationsFeature.cs`
  pages active pending invitations, using one captured UTC instant for both the
  count and row query.
- `src/modules/iam/TenantForge.Modules.Iam/features/audit/AuditFeature.cs`
  pages audit events after applying `action` and `fromUtc` filters.
- `tests/integration/TenantForge.Api.IntegrationTests/PaginationIntegrationTests.cs`
  adds focused PostgreSQL-backed coverage for the shared contract across all
  seven endpoints.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs`
  adds an isolated pagination test database so the >50-row data does not pollute
  unrelated tests.
- `docs/design/s15-list-pagination.md`
  records the final request/response contract and endpoint inventory for F022.
- `tasks/TASKS.md`
  tracks B015 through the task lifecycle.

## 2. Request flow from endpoint to response

1. The endpoint performs its existing authentication and authorization checks.
   Platform endpoints still use the platform-admin policy; tenant endpoints still
   validate tenant membership and permissions first.
2. The endpoint calls `PaginationSupport.TryBind(HttpRequest, ...)`.
3. `TryBind` reads `pageNumber` and `pageSize` from the query string. Missing
   values use independent defaults: page 1 and size 50.
4. Invalid empty, non-numeric, zero, negative, out-of-range, too-large or offset
   overflow inputs return `400` with a field-specific validation problem.
5. The endpoint builds its scoped query: tenant/account authorization, business
   filters, invitation active filtering or audit `action`/`fromUtc` filtering are
   applied before pagination.
6. `PaginationSupport.PageAsync` runs `CountAsync()` on the scoped query.
7. The endpoint applies its stable order, then SQL `Skip(offset)` and
   `Take(pageSize)`.
8. The endpoint returns the existing collection property plus sibling
   `pagination` metadata.

Example shape:

```json
{
  "users": [],
  "pagination": {
    "pageNumber": 2,
    "pageSize": 20,
    "totalCount": 45,
    "totalPages": 3,
    "hasPreviousPage": true,
    "hasNextPage": true
  }
}
```

## 3. Backend concepts introduced

- **Count-before-page:** `totalCount` is the number of rows that match the
  authorized scoped query before `Skip`/`Take`, not the number of rows returned
  on the current page.
- **Stable offset pagination:** each endpoint preserves its existing sort order
  and adds/keeps a unique tie-breaker so adjacent pages are deterministic while
  the data is unchanged.
- **Scoped totals:** platform, tenant, permission and filter rules apply to both
  the count and the data. A caller never receives counts for data they cannot
  see.
- **Bounded query parameters:** invalid query strings fail at the API boundary;
  no bad `pageSize` becomes an unbounded query and huge offsets do not overflow
  into a 500.
- **Paged reads versus snapshots:** paged collection reads are separate from
  complete security snapshots such as resolved permissions, role mutation
  refreshes and last-administrator checks.

## 4. Important security decisions

- **No count leaks:** ordinary users still receive `403` from platform lists and
  no pagination object is returned with denied responses.
- **Tenant scope first:** my-tenants, members, roles, invitations and audit count
  only rows inside the caller's allowed tenant/account scope.
- **Denied later pages stay denied:** a forbidden request with `pageNumber=2` is
  still forbidden; pagination does not bypass membership or permission checks.
- **Complete role payloads:** paged role reads return complete `permissionKeys`
  and `memberIds` for each returned role, while authorization and administrator
  protection continue to consider all roles.
- **Audit and invitation filters are consistent:** audit filters affect totals;
  invitation totals exclude expired, nonpending and other-tenant rows.

## 5. Alternatives deliberately postponed

- No cursor pagination; offset paging is enough for F022's tables and selectors.
- No repository framework or generic query engine; the implementation stays
  endpoint-local and explicit.
- No new endpoint, search, sort selector, export or schema redesign.
- No frontend implementation; F022 owns visible controls and URL state.
- No attempt to promise a cross-request snapshot under concurrent inserts or
  deletes.

## 6. Commands and manual steps to verify

Commands run from the repository root:

```text
dotnet.exe build tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~PaginationIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --no-build
dotnet.exe build src/api/TenantForge.Api/TenantForge.Api.csproj
```

Observed results:

- Focused pagination tests: 4/4 passed.
- Full integration suite: 82/82 passed.
- API build: 0 warnings, 0 errors.

Manual same-origin API demo:

1. Start PostgreSQL with `docker compose up -d postgres`.
2. Start the API with `dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000`.
3. Curl through the WSL gateway IP.
4. Log in as the seeded platform admin.
5. Demonstrate platform users page 2 and platform tenants page 2 with metadata.
6. Create a tenant owner, create enough role/audit data, then request audit page
   2 filtered to `Role.Created`.
7. Request a beyond-last roles page and observe an empty `roles` array with the
   requested valid page metadata.
8. Request invalid `pageNumber=0` and observe a `400` with `pageNumber` errors.
9. Use a non-admin token on `/api/platform/users?pageNumber=2&pageSize=10` and
   observe `403` with no pagination leak.
10. Stop the API and confirm health returns `000`.

## 7. Review questions

1. Why must `totalCount` be calculated after authorization and business filters
   but before `Skip`/`Take`?
2. What user-visible bugs could appear if two rows on adjacent pages had the same
   primary sort value and no unique tie-breaker?
3. Why do role mutation responses still return a complete role snapshot instead
   of reusing the paged role read response?
