# S01 — Development-only hardcoded admin login

## Status

`Planned`

## Owner

`backend-mentor` implements the fixed contract, then `ui-engineer` replaces the S00 mock.

## Depends on

- S00.

## Visible outcome

The platform administrator enters development credentials on the existing login screen and reaches the dashboard through a real .NET API. Wrong credentials produce the existing invalid-credentials UI.

## Demo

1. Start the API and web application in Development.
2. Sign in with the documented local administrator credentials.
3. Observe the dashboard and an authenticated access token held by the chosen client adapter.
4. Sign out or clear local task state.
5. Submit a wrong password and observe `401` feedback.
6. Start the API using Production configuration and prove the development authentication stub cannot be enabled.

## UI states

- Existing S00 idle and validation states.
- Real request in progress.
- `401` invalid credentials.
- Unavailable API error distinct from invalid credentials.
- Successful navigation.

## API contract

### `POST /api/auth/login`

Request:

```json
{
  "email": "admin@tenantforge.local",
  "password": "local-development-password"
}
```

Success `200`:

```json
{
  "accessToken": "signed-token",
  "expiresAtUtc": "2030-01-01T00:00:00Z",
  "user": {
    "id": "development-admin",
    "email": "admin@tenantforge.local",
    "displayName": "Platform Administrator",
    "isPlatformAdmin": true
  }
}
```

Invalid credentials return `401` with the project's standard problem-details shape introduced by this task.

## Backend learning goal

Understand the full Minimal API request path: endpoint mapping, request validation, environment-gated credential check, signed token creation, HTTP result and integration test.

## Scope

- Create the minimum .NET solution, API host and IAM module needed by this endpoint.
- Add a health endpoint only if required to run or coordinate the two applications.
- Keep credentials in Development configuration or user secrets, never production configuration.
- Register the development credential checker only in Development.
- Fail startup when development authentication is configured outside Development.
- Return a real signed, expiring token containing only claims required by current slices.
- Replace the frontend mock adapter with an HTTP implementation.
- Add integration tests for success, wrong credentials and production fail-closed behavior.
- Write `docs/learning/s01-hardcoded-admin-login.md`.

## Out of scope

- Database, EF Core and user entity.
- Password hashing or account lockout.
- Refresh tokens and token rotation.
- Registration and password recovery.
- Roles, permissions and tenants beyond the single platform-admin claim.

## Acceptance criteria

- [ ] Existing login UI uses the real endpoint with no visual regression.
- [ ] Valid Development credentials return `200` and a signed expiring token.
- [ ] Wrong credentials return `401` without identifying which field was wrong.
- [ ] API-unavailable feedback is not shown as invalid credentials.
- [ ] Password and token values never appear in logs.
- [ ] The development checker is unavailable and fails closed outside Development.
- [ ] Integration tests and browser demo pass.
- [ ] Backend learning note explains the complete request flow.
- [ ] No persistence, registration, refresh token, tenant or permission work is included.
