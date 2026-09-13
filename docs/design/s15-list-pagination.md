# S15 list pagination contract

B015 adds one shared pagination contract to the seven existing business collection
GET endpoints. It preserves every existing collection key and row payload so
current clients can still read the default first page before F022 adds visible
controls.

## Request query

Every endpoint listed below accepts these query parameters:

| Field | Default | Valid range | Notes |
|---|---:|---:|---|
| `pageNumber` | `1` | `1..int.MaxValue` | One-based page number. |
| `pageSize` | `50` | `1..100` | Requested number of rows. |

Omitted values use defaults independently. Empty, non-integer, zero, negative,
out-of-range, `pageSize > 100`, and offset overflow return `400` with a
ValidationProblem entry for the matching query field. Valid pages beyond the
last page return an empty collection and the requested page metadata.

Audit also keeps its existing optional filters:

```text
GET /api/tenants/{tenantId}/audit?action=Role.Created&fromUtc=2026-09-01T00%3A00%3A00Z&pageNumber=2&pageSize=20
```

`action` and `fromUtc` are validated as before, then applied before counting and
paging.

## Response metadata

Every paged response keeps its existing collection property and adds a sibling
`pagination` object with exactly these fields:

```json
{
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

Rules:

- `totalCount` is the number of authorized, matching rows before `Skip`/`Take`.
- `totalPages` is `ceil(totalCount / pageSize)` and is `0` when there are no
  matches.
- `hasPreviousPage` is `pageNumber > 1`.
- `hasNextPage` is `pageNumber < totalPages`.
- The returned `pageNumber` and `pageSize` are the requested valid values, even
  when the page is empty or beyond the last page.

## Endpoint inventory

| Endpoint | Collection key | Ordering | Security/scope |
|---|---|---|---|
| `GET /api/platform/users` | `users` | `createdAtUtc ASC, id ASC` | Platform admin only. |
| `GET /api/platform/tenants` | `tenants` | `createdAtUtc ASC, id ASC` | Platform admin only. |
| `GET /api/auth/me/tenants` | `tenants` | `name ASC, id ASC` | Caller’s active tenant memberships only. |
| `GET /api/tenants/{tenantId}/members` | `members` | `displayName ASC, email ASC, id ASC` | Active tenant membership required. |
| `GET /api/tenants/{tenantId}/roles` | `roles` | `name ASC, id ASC` | Active tenant membership required. |
| `GET /api/tenants/{tenantId}/invitations` | `invitations` | `createdAtUtc DESC, id ASC` | `IAM.Invitations.View` or Owner. |
| `GET /api/tenants/{tenantId}/audit` | `events` | `createdAtUtc DESC, id DESC` | `IAM.Audit.View` or Owner. |

Members still return the sibling `tenant` context even when the requested page
has no members. Roles still return complete `permissionKeys` and complete
`memberIds` for every returned role.

## Database and security rules

- Authorization, tenant scope, permission checks, action/fromUtc filters and
  invitation active filters apply before `CountAsync`.
- Data queries use deterministic ordering followed by SQL `Skip`/`Take`; they do
  not materialize all rows before paging.
- Invitation count and rows use the same captured UTC instant for active
  filtering.
- Platform endpoints never expose rows or counts to ordinary users.
- Other-tenant, missing-membership and missing-permission requests stay denied on
  later pages.
- Permission catalog, resolved permissions, dashboard aggregates, authorization
  checks, administrator-protection checks and mutation response refreshes remain
  unpaged complete snapshots.

## Examples

```text
GET /api/platform/users?pageNumber=2&pageSize=20
GET /api/auth/me/tenants?pageSize=100
GET /api/tenants/{tenantId}/members?pageNumber=999&pageSize=10
GET /api/tenants/{tenantId}/audit?action=Invitation.Created&pageNumber=1&pageSize=10
```

## Intermediate UI limitation

Until F022 lands, the existing browser screens may continue to render only the
default first page, but their existing array keys remain present so they keep
working. Complete visible pagination controls, selector navigation and URL state
belong to F022.

## Out of scope

- No cursor pagination.
- No exports.
- No new endpoint.
- No schema redesign.
- No authorization-policy change.
- No frontend implementation.
