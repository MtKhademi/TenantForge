# S11 platform and tenant access boundaries contract

B012 freezes the backend contract that F018 must consume without shape changes.

## Contract decision (supersedes an S09 example)

S09's role-permission-matrix design (`docs/design/s09-role-permission-matrix.md`)
illustrated a tenant "User Manager" custom role granting `IAM.Users.View` /
`IAM.Users.Create` and used the **platform** `GET/POST /api/platform/users`
endpoints as the example screen those permissions unlocked. That example is
deliberately superseded by this slice: platform account creation and the
global account directory belong exclusively to a platform administrator.
Tenant user provisioning is not being added. A tenant `tenantId` — whether
supplied by an Owner, a Member or a custom role holding `IAM.Users.View` /
`IAM.Users.Create` — no longer grants any access to `/api/platform/users`.
The permission keys `IAM.Users.View` / `IAM.Users.Create` remain valid,
assignable tenant-role permissions (unchanged catalog, unchanged role CRUD);
they simply no longer gate a platform endpoint. No tenant-scoped user-creation
endpoint is introduced to replace that use — this is a closed boundary, not a
relocated feature.

## API contract

### `GET /api/platform/users`, `POST /api/platform/users`

Both endpoints require `AuthorizationPolicyNames.PlatformAdmin`
(`isPlatformAdmin == "true"` claim). This is now the **only** authorization
path:

- Unauthenticated caller → `401`.
- Authenticated, non-admin caller → `403`, regardless of whether `tenantId` is
  omitted, is the caller's own membership, or names a foreign tenant. A
  supplied `tenantId` has no effect on this decision.
- Platform administrator → unchanged success behavior: `GET` lists accounts,
  `POST` creates one. Success, `400` validation and `409` duplicate-email
  payloads are unchanged from B006/B009.
- A denied read exposes no account/identity fields; a denied create persists
  no account.

### `GET /api/auth/me/tenants` (new)

Authenticated (any account, including a platform administrator). Returns the
caller's own active-tenant memberships — never a bypass, even for an admin.

`200`:

```json
{
  "tenants": [
    {
      "id": "5c9e...",
      "name": "Acme",
      "slug": "acme",
      "status": "Active",
      "membershipRole": "Member"
    }
  ]
}
```

Field semantics:

| Field | Notes |
|---|---|
| `id` | Tenant id. |
| `name` | Tenant display name. |
| `slug` | Tenant slug. |
| `status` | Always `"Active"` in this response — memberships in a `Suspended` tenant are excluded, not returned with a suspended status. |
| `membershipRole` | `"Owner"` or `"Member"` — the caller's own `TenantMembership.Role` for that tenant. Not the tenant-role/permission system; a plain membership-kind label. |

Rules:

- Only tenants where `Tenant.Status == Active` **and** a `TenantMembership` row
  for the caller exists are returned. A suspended tenant is excluded even if a
  membership row exists. A tenant the caller left (no membership row) is
  naturally excluded — this schema has no soft-deleted membership state, so
  "excluded deleted membership" reduces to "row absent."
- A third tenant the caller does not belong to is never returned.
- Ordered by tenant name, then tenant id (ties broken deterministically); no
  duplicates (unique membership per `(tenantId, accountId)` already enforced
  by the persistence layer).
- No memberships → `{"tenants": []}` (empty array, not `404`/`null`).
- Missing/invalid JWT → `401`.
- The `sub` claim is verified against `Accounts` server-side: a missing,
  unparseable or `Disabled` account receives `403` even if its JWT is still
  signature-valid (stateless tokens outlive row state). Fail-closed, default
  deny.
- A platform administrator receives exactly their own memberships, computed
  the same way as any other caller — no enumeration of tenants without a
  membership row.

## What is out of scope for B012

- Any new tenant-scoped account/member creation endpoint.
- Changes to `/api/tenants/{tenantId}/members`, tenant roles, or the
  permission catalog shape.
- Frontend navigation/switcher behavior (F018 consumes this contract).
- Pagination (`GET /api/platform/users` keeps its existing first-page-only
  behavior; pagination is B015).
