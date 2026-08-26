# S08 — Tenant isolation

## Executable tasks

- Backend: `B008`
- Front: `F012`

## Owner

`backend-mentor`, then `ui-engineer`.

## Depends on

- S07.

## Visible outcome

The administrator can switch between two tenants and see different member lists. A user who is not a member of the selected tenant receives a clear `403` page, while the API proves cross-tenant data cannot be read.

## Demo

1. Create tenants `Acme` and `Globex` with different members.
2. Open Acme members and note its list.
3. Switch to Globex and see a different list.
4. Use an account with no Globex membership and attempt the same route.
5. Observe the UI `403` and API denial.

## UI states

- Tenant context resolving.
- Tenant member list loading/loaded.
- Invalid tenant selection.
- Forbidden tenant page.

## API contract

### `GET /api/tenants/{tenantId}/members`

Returns members only when the authenticated user has active membership or platform support access explicitly defined by the task. Missing authentication returns `401`; missing tenant access returns `403`; unknown tenant returns the agreed non-leaking result.

## Backend learning goal

Understand trustworthy tenant-context resolution, membership authorization, tenant-scoped queries and adversarial integration tests for horizontal access control.

## Scope

- Resolve tenant context from explicit route identity plus authenticated membership.
- Add tenant-member query and page.
- Enforce tenant filtering on the server.
- Add adversarial integration tests that attempt cross-tenant reads.
- Add route-level forbidden UI and recovery to an available tenant.
- Write `docs/learning/s08-tenant-isolation.md`.

## Out of scope

- Roles beyond Owner semantics.
- Tenant edit/suspension.
- Direct database row-level security.
- Permission cache.
- Tenant-specific business modules.

## Acceptance criteria

- [ ] Two tenants show distinct real member lists.
- [ ] Non-member access is denied by the API even if the client changes the route manually.
- [ ] Missing tenant context fails closed.
- [ ] Cross-tenant integration tests verify both reads and identifier tampering.
- [ ] UI provides a useful `403` state without presenting hidden data.
- [ ] Browser demo and all tests pass.
- [ ] Learning note explains why UI tenant selection is not authorization.
