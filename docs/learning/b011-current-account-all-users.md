# B011 — Current account for all authenticated users

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/features/account/CurrentAccountFeature.cs` removes the platform-admin check from the `/api/auth/me` identity validation block.
- `tests/integration/TenantForge.Api.IntegrationTests/CurrentAccountIntegrationTests.cs` adds a non-admin authenticated token test while keeping admin and unauthenticated coverage.
- `tasks/TASKS.md` tracks this backend slice through `in_progress` and then `review`.

## 2. Request flow from endpoint to response

1. The browser calls `GET /api/auth/me` with a bearer token.
2. ASP.NET Core validates the token before the endpoint runs because the route uses `.RequireAuthorization()`.
3. `CurrentAccountFeature` reads `sub`, `email`, `name` and `isPlatformAdmin` from the validated claims principal.
4. Missing required identity claims still return `401`.
5. Any authenticated account with the required identity claims receives `200`; the response includes `isPlatformAdmin` as data for the UI.

## 3. Backend concepts introduced

This slice separates authentication from authorization:

- Authentication proves the caller has a valid server-issued token.
- The current-account endpoint answers "who am I?" for session bootstrap.
- Authorization decisions, such as platform-admin-only actions or tenant-scoped access, belong on the protected business endpoints that need those permissions.

## 4. Important security decisions

- The endpoint still requires authentication and never trusts client-provided identity fields.
- Required identity claims still fail closed with `401` when missing.
- `isPlatformAdmin` remains in the response but no longer gates the ability to learn the current authenticated identity.
- Non-admin users are not granted admin rights by this change; they only keep a valid browser session.

## 5. Alternatives deliberately postponed

- No new permissions system was added here; role/permission APIs remain future slices.
- No tenant authorization was moved into `/api/auth/me`; tenant membership checks stay on tenant-scoped endpoints.
- No refresh-token or logout revocation behavior was added.

## 6. Commands and manual steps to verify

Commands:

```text
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter CurrentAccountIntegrationTests
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
dotnet.exe build
```

Manual demo handoff:

1. Build and run the API used by the frontend demo.
2. Sign in as a non-admin tenant member.
3. Refresh the browser so the app calls `GET /api/auth/me`.
4. Confirm the session remains active and `isPlatformAdmin` is `false` in the current-account response.
5. Confirm admin-only and tenant-scoped actions are still decided by their own endpoints.

## 7. Review questions

1. Why is `/api/auth/me` an authentication/session-bootstrap endpoint rather than an admin authorization endpoint?
2. Which checks still make the endpoint fail closed for anonymous, invalid or malformed identities?
3. Where should platform-admin and tenant-membership authorization be enforced instead?
