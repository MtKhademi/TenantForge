# S09 — Role and permission matrix

## Executable tasks

- Front contract and mock: `F011`
- Backend: `B009`
- Front integration: `F012`

## Owner

`ui-engineer`, then `backend-mentor`, then `ui-engineer`.

## Depends on

- S08.

## Visible outcome

A tenant Owner opens Roles, creates a custom role, selects permissions in a grouped matrix and assigns that role to a tenant member. The member can open allowed pages and receives `403` for denied operations.

## Demo

1. Enter Acme as Owner and open Roles.
2. Create `User Manager`.
3. Enable `IAM.Users.View` and `IAM.Users.Create` in the matrix.
4. Assign the role to an Acme member.
5. Sign in as that member and see permission-aware navigation.
6. Open an allowed page successfully.
7. Attempt an ungranted action through the API and see `403`.

## UI states

- Role list loading/empty/loaded.
- Role editor and grouped permission matrix.
- Unsaved, saving, validation and success states.
- Permission-aware navigation.
- Forbidden operation.

## API contract

The task may use up to two command/query groups while keeping endpoints resource-oriented:

- list permission catalog and tenant roles;
- create/update a tenant role with permission keys;
- assign a role to a tenant member;
- expose resolved current-tenant permissions through the authenticated context.

Exact request shapes are frozen by the UI mock before backend implementation.

## Backend learning goal

Understand permission catalogs, tenant-owned roles, role assignment, resolved authorization and the difference between navigation visibility and server policies.

## Scope

- Add stable permission keys required by existing screens only.
- Add custom tenant roles and role-permission assignments.
- Assign roles to tenant members.
- Enforce at least one read and one write permission through API policies.
- Return resolved permissions for current tenant UI behavior.
- Prevent removing or disabling the last effective Owner.
- Add integration and browser tests.
- Write `docs/learning/s09-role-permission-matrix.md`.

## Out of scope

- Direct per-user allow/deny overrides.
- Permission cache and distributed invalidation.
- Platform custom roles.
- Impersonation.
- Wildcard permissions.

## Acceptance criteria

- [ ] Role and matrix UI persist a custom role for one tenant.
- [ ] The same role name can exist in another tenant without sharing permissions.
- [ ] Assigned member receives the expected resolved permission keys.
- [ ] Allowed API behavior succeeds and ungranted behavior returns `403`.
- [ ] Manually exposing a hidden UI control cannot bypass the API.
- [ ] Last-Owner protection is tested.
- [ ] Integration, frontend and browser tests pass.
- [ ] Learning note explains catalog, resolution and policy flow.
