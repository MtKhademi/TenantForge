# S15 — Server pagination for lists and their UI consumers

## Owners and visible outcome

- B015: backend-mentor; collection query contract, database paging and tests.
- F022: ui-engineer; Persian RTL page controls and all existing list consumers.

Users can reach records beyond the first page, choose a page size and see the
filtered total. Audit filters and page selection work together. Dropdowns and
the tenant switcher also reach later records without downloading every page.

## Current problem and endpoint inventory

Users, platform tenants, invitations and audit currently use a fixed Take(50)
without totals. Members and roles materialize all rows. S11 adds my tenants
as an unpaged list. All seven business collection GETs below must accept the
same pagination inputs and return the same pagination metadata:

| GET endpoint | Existing collection key | Existing ordering to preserve |
|---|---|---|
| /api/platform/users | users | createdAtUtc ASC, id ASC |
| /api/platform/tenants | tenants | createdAtUtc ASC, id ASC |
| /api/auth/me/tenants | tenants | name ASC, id ASC |
| /api/tenants/{tenantId}/members | members | displayName ASC, email ASC, id ASC |
| /api/tenants/{tenantId}/roles | roles | name ASC, id ASC (add unique tie-breaker) |
| /api/tenants/{tenantId}/invitations | invitations | createdAtUtc DESC, id ASC |
| /api/tenants/{tenantId}/audit | events | createdAtUtc DESC, id DESC |

The permission catalog, resolved permissions and nested permissionKeys/memberIds
are complete configuration/security snapshots, not independently browsed record
collections. Do not paginate them or derive authorization from a loaded page.
Dashboard aggregates and single-resource/mutation responses remain unchanged.

## Shared request contract

- Every endpoint binds a query/filter DTO containing pageNumber and pageSize;
  reuse small shared pagination primitives inside IAM. The audit filter DTO
  additionally retains action and fromUtc with their current semantics.
- pageNumber is a one-based integer, default 1. pageSize is an integer from
  1 through 100, default 50. Omitted values use defaults independently.
- Invalid supplied values (empty, non-integer, zero, negative, outside the
  integer range or pageSize > 100) return 400 ValidationProblem with the
  matching query field. Do not silently clamp invalid input.
- Validate offset arithmetic before querying: an offset exceeding the supported
  EF Skip integer range returns a pageNumber validation error, never overflow
  or a 500. A representable page beyond the data returns an empty page.
- Example: GET /api/tenants/{tenantId}/audit?action=Role.Created&fromUtc=2026-09-01T00%3A00%3A00Z&pageNumber=2&pageSize=20.
- No GET request body. No new search, sorting or business filters are required;
  all existing predicates apply before counting and paging.

## Shared response contract and rollout

Keep each existing collection property and row shape; add a sibling pagination
object with exactly these required fields (example: 45 matches, second page):

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

The example omits row contents; an actual second page in this case has 20 rows.
Members retain their sibling tenant object even on empty pages. totalCount is
the count of matching, authorized rows before Skip/Take, never the page length.
totalPages = ceiling(totalCount / pageSize), with 0 for no matches.
hasPreviousPage = pageNumber > 1; hasNextPage = pageNumber < totalPages.
Return the requested valid pageNumber/pageSize even for empty or out-of-range
pages. Do not replace them with the last page or calculate counts globally.

Authorization and tenant/account scope apply to both count and data queries.
Use a stable unique order, database-side Count/Skip/Take and cancellation.
Invitation count and rows share one captured UTC instant for active/expiry
filtering. Role assignment reads are restricted to the returned role IDs without
truncating each returned role's memberIds or permissionKeys.

This deliberately supersedes the fixed first-page limits and the S11 promise
to return all memberships. B015 owns the updated current contract document;
historical slices remain history. Existing clients can parse the retained array
keys until F022 lands, but their lists/selectors expose only the default page.
Do not claim the complete UI outcome before F022. Deliver this pair in order;
do not bundle application changes into task registration.

Offset paging reflects live data: concurrent inserts/deletes can move records
between requests. Do not promise a cross-request snapshot or add cursor paging.

## Frontend behavior and consumer inventory

- Page users, tenants, members, invitations and audit tables, plus the role
  list/cards, from the server. A shared PaginationQuery/PaginationMeta model
  and reusable accessible control avoid six incompatible implementations.
- Offer page sizes 10, 20, 50 and 100; use 20 for a new table view. Show previous,
  next, current/total pages and the visible range out of totalCount in Persian.
- Store table pageNumber/pageSize with its filters in the route query string.
  Reload and browser Back/Forward restore them. Invalid URL values fall back
  to defaults before a request. Switching filters, page size or tenant resets
  to page 1 atomically; switching accounts clears all previous scoped data.
- Preserve audit action/fromUtc when paging. Use totalCount for list totals;
  do not label a page length or page-only role-kind count as the overall total.
- Treat an empty dataset, no filter matches, loading, 400, 401, 403 and a
  retryable network failure distinctly. Discard superseded responses; never
  flash records from a previous tenant/account. A failed request must not make
  old rows appear under a new successful page label.
- After a mutation, refetch affected pages and totals under the active filters;
  do not prepend rows beyond pageSize. If deletions/expiry leave a requested
  page beyond totalPages, move to max(1, totalPages) with a bounded recovery
  refetch. Avoid loops under continuing concurrent changes.
- TenantSwitcher/my tenants, the tenant-creation Owner selector, invitation role
  selector and role/member assignment controls must offer explicit next/previous
  or load-more navigation using bounded requests. Keep selected identities and
  labels when they are off-page. Never load all pages automatically or use a
  first-page absence as proof a tenant, user, member or role is inaccessible.
- Keep tenant-management table state independent from switcher option state.
  Existing detail/context reads authorize deep links even when the selected
  tenant is absent from the currently loaded discovery page. Complete resolved
  permissions continue to control actions independently of paged role data.

## Demo and evidence

Use reproducible local/test data covering more than 50 matches for every
collection, including my tenants and roles; do not expand normal production
seeding. Verify first/middle/last/empty pages and reach an ID beyond the old cap.
Show audit filtering with at least two matching pages and a correct filtered
total. In a real browser, demonstrate table controls, reload/Back, a later-page
Owner/custom role/tenant choice, rapid tenant switching and a denied request.
Use smaller page sizes to make the demo compact; retain >50 test coverage.

No export, cursor paging, new endpoint, schema redesign, authorization-policy
change or frontend test ownership change is included.
