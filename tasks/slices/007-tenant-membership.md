# S07 — Tenant and membership

## Executable tasks

- Front contract and mock: `F008`
- Backend: `B007`
- Front integration: `F009`

## Owner

`ui-engineer`, then `backend-mentor`, then `ui-engineer`.

## Depends on

- S06.

## Visible outcome

The platform administrator creates a tenant, assigns an existing user as Owner and sees the tenant in the application tenant switcher.

## Demo

1. Open Platform Tenants and create `Acme`.
2. Assign an existing user as the first Owner.
3. See `Acme` in the tenant list.
4. Use the tenant switcher and enter the tenant-scoped shell.
5. Attempt creation without an owner and see validation feedback.

## UI states

- Tenant list loading, empty and loaded.
- Create-tenant validation, submitting, conflict and success.
- Tenant switcher with selected state.
- Tenant shell loading.

## API contract

### `GET /api/platform/tenants`

Returns tenant summaries with `id`, `name`, `slug`, `status`, `memberCount` and `createdAtUtc`.

### `POST /api/platform/tenants`

Accepts `name`, `slug` and `ownerUserId`. Creates the tenant and Owner membership atomically.

Tenant selection is represented in a stable client context agreed by this task; it must not itself grant access.

## Backend learning goal

Understand aggregate boundaries, atomic tenant-plus-owner creation, membership identity and the difference between selecting a tenant and being authorized for it.

## Scope

- Introduce Tenant and Membership models.
- Create a tenant with exactly one initial Owner in one transaction.
- Enforce unique normalized slug.
- Add platform tenant list and create pages.
- Add the first functional tenant switcher and tenant-scoped shell.
- Add integration and browser tests.
- Write `docs/learning/s07-tenant-membership.md`.

## Out of scope

- Second tenant isolation proof.
- Editing, suspending or deleting tenants.
- Invitations or membership removal.
- Custom roles and permission matrix.
- Tenant-specific business data.

## Acceptance criteria

- [ ] Platform admin creates a tenant and Owner atomically.
- [ ] Missing/invalid owner and duplicate slug fail safely.
- [ ] Tenant appears after refresh and in the switcher.
- [ ] Selecting a tenant does not bypass server membership checks.
- [ ] Tenant creation is platform-admin protected.
- [ ] Integration and browser tests pass.
- [ ] Learning note explains tenant, membership and transaction boundaries.
