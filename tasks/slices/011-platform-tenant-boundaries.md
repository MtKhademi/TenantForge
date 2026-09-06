# S11 — Platform and tenant access boundaries

## Executable tasks and owner
- B012: backend-mentor; platform authorization and current-account tenant discovery.
- F018: ui-engineer; integrate discovery and scope-aware navigation.

## Context and contract decision
Review baseline: main at 8b5281e. Tenant permissions currently authorize the
global Accounts query in GET /api/platform/users. The frontend does not send
tenantId and loads the platform tenant list for every account.
Platform account creation and the global directory belong exclusively to a
platform admin. Tenant membership is discovered separately. This deliberately
supersedes the S09 example that allowed a tenant User Manager to create platform
accounts; tenant user provisioning is not being added.

## Visible outcome and demo
1. As platform admin, open Users and create an ordinary account.
2. Sign in as an ordinary account belonging to two tenants and choose either
   tenant from the shell without a platform-list 403.
3. Open /users directly: show a Persian access-denied state.
4. Request the platform users API with an owned tenantId: receive 403 and no data.
5. A signed-in account with no memberships sees a useful empty state.

## API contract
- Existing GET/POST /api/platform/users keep successful payloads and admin
  behavior; non-admin callers receive 403, including when tenantId is supplied.
- Existing platform dashboard and tenant-management APIs stay admin-only.
- New GET /api/auth/me/tenants returns 200:
  {"tenants":[{"id":"guid","name":"Acme","slug":"acme","status":"Active","membershipRole":"Member"}]}.
  Return all of the caller's active-tenant memberships, ordered by name then id,
  without duplicates; membershipRole is Owner or Member. No memberships is an
  empty array. Missing/invalid JWT is 401; a missing/disabled account is 403.
  A platform admin also receives only their memberships here, not a bypass.
- Existing /api/tenants/{tenantId}/members remains the tenant entry read.
  No extra tenant account creation endpoint.

## Acceptance and boundaries
Demonstrate admin success, ordinary-member navigation and direct API denial.
Integration coverage must inspect returned identities, not only status codes.
F018 consumes the single new endpoint. No invitation acceptance, platform custom
roles, dashboard redesign or new registration flow.
B012 owns docs/design/s11-platform-tenant-boundaries.md and its learning note.
