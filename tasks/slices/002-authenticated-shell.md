# S02 — Authenticated application shell

## Executable tasks

- Backend: `B002`
- Front: `F003`

## Owner

`backend-mentor`, then `ui-engineer`.

## Depends on

- S01.

## Visible outcome

Refreshing a valid dashboard restores the administrator identity, protected routes reject missing or expired authentication, and logout returns the user to login.

## Demo

1. Sign in and open `/dashboard`.
2. Refresh the browser and see the administrator name restored.
3. Use logout and return to `/login`.
4. Navigate directly to `/dashboard` without authentication and return to login.
5. Simulate an invalid or expired token and see a clean session-expired flow.

## UI states

- Authentication bootstrap/loading.
- Authenticated shell with current user.
- Anonymous redirect.
- Session expired.
- Logout in progress.

## API contract

### `GET /api/auth/me`

Success `200`:

```json
{
  "id": "development-admin",
  "email": "admin@tenantforge.local",
  "displayName": "Platform Administrator",
  "isPlatformAdmin": true
}
```

Missing, invalid or expired authentication returns `401`.

Logout is initially a client operation because S01 has no server-side session. The task documents that limitation instead of inventing token revocation.

## Backend learning goal

Understand ASP.NET Core authentication middleware, claim validation, protected endpoints, current-user mapping and the difference between client logout and server-side revocation.

## Scope

- Configure authentication middleware for the S01 token.
- Add the protected current-account endpoint.
- Introduce a frontend authentication provider and protected-route boundary.
- Restore current account state on application startup.
- Clear client authentication state on logout and `401`.
- Add integration tests for valid, missing, malformed and expired authentication.
- Write `docs/learning/s02-authenticated-shell.md`.

## Out of scope

- Refresh token, server-side session and revocation.
- Persistent database user.
- Authorization beyond authenticated/platform-admin distinction.
- Dashboard data.

## Acceptance criteria

- [ ] Refreshing an authenticated page restores the current administrator.
- [ ] Anonymous direct navigation never flashes protected dashboard content.
- [ ] Invalid and expired authentication produce `401` and clear client state.
- [ ] Logout clears client state and returns to login.
- [ ] `/api/auth/me` never trusts client-provided identity fields.
- [ ] Integration and browser tests pass.
- [ ] Learning note explains middleware, claims and client-only logout limitations.
