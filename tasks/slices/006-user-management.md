# S06 — User management

## Executable tasks

- Front contract and mock: `F006`
- Backend: `B006`
- Front integration: `F007`

## Owner

`ui-engineer` defines and mocks the page contract, `backend-mentor` implements it, then `ui-engineer` integrates it.

## Depends on

- S05.

## Visible outcome

The platform administrator opens a Users page, sees persisted accounts and creates a basic active user through a validated form.

## Demo

1. Sign in and open Users from the sidebar.
2. See the seeded administrator in the table.
3. Open Create user, submit invalid input and see field errors.
4. Create a valid user and see it in the table.
5. Try the same email again and see a conflict message.

## UI states

- List loading, loaded and empty.
- Create form idle, invalid, submitting and success.
- Duplicate-email conflict.
- Retryable list error.

## API contract

### `GET /api/platform/users`

Returns a first-page collection with stable fields: `id`, `email`, `displayName`, `status`, `isPlatformAdmin`, `createdAtUtc`.

### `POST /api/platform/users`

Accepts `email`, `displayName` and an initial password. Returns the created user without secret material.

Both endpoints require platform-administrator authorization.

## Backend learning goal

Understand query/command separation in a vertical slice, validation, uniqueness conflicts, password hashing and mapping entities to safe API responses.

## Scope

- Add Users navigation and page.
- Implement list and create contracts with bounded initial pagination.
- Validate email, display name and password server-side and client-side.
- Hash the initial password and normalize email.
- Return a stable conflict problem for duplicate email.
- Add integration and browser coverage.
- Write `docs/learning/s06-user-management.md`.

## Out of scope

- Edit, delete, password reset or bulk operations.
- Search, advanced filters and arbitrary sorting.
- Tenant membership and tenant roles.
- Invitations.

## Acceptance criteria

- [ ] Seeded administrator appears through the real API.
- [ ] A valid user is persisted and appears after refresh.
- [ ] Server validation is authoritative and maps to clear UI errors.
- [ ] Duplicate email returns a conflict without exposing internals.
- [ ] Password never appears in query responses or logs.
- [ ] Non-admin access is denied by the API.
- [ ] Integration, frontend and browser tests pass.
- [ ] Learning note explains command/query request paths.
