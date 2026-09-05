# B006 — S06 user management API: learning note

## Files changed and why

### New

- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs` — the B006 vertical slice. It maps the platform-user list and create endpoints, validates create requests, hashes the initial password, handles duplicate normalized emails and maps persisted accounts to safe response models.
- `tests/integration/TenantForge.Api.IntegrationTests/UserManagementIntegrationTests.cs` — API-level coverage for the happy path and the relevant failure paths: list, create-and-refresh, invalid input, duplicate email, missing authentication and authenticated non-admin authorization failure.

### Modified

- `src/modules/iam/TenantForge.Modules.Iam/domain/Account.cs` — adds `CreateUser(...)`, a domain factory for a normal active account. It mirrors the seeded-admin factory but always sets `IsPlatformAdmin = false`.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — maps the new users feature through the IAM public seam. The API host still composes IAM only through `IamModule`.
- `tasks/TASKS.md` — B006 moved through the task lifecycle for this slice.

## Request flow from endpoint to response

### `GET /api/platform/users`

1. The browser calls `GET /api/platform/users` with a Bearer token.
2. ASP.NET Core authenticates the JWT and runs the existing `PlatformAdmin` authorization policy.
3. The endpoint queries `IamDbContext.Accounts` with `AsNoTracking()`, orders by `CreatedAtUtc` and `Id`, and limits the first page to 50 rows.
4. Accounts are mapped in memory to `UserResponse` so the API returns only `id`, `email`, `displayName`, `status`, `isPlatformAdmin` and `createdAtUtc`.
5. The response is `200 OK` with `{ users: [...] }`.

### `POST /api/platform/users`

1. The browser calls `POST /api/platform/users` with `email`, `displayName` and `password`.
2. Authentication and `PlatformAdmin` authorization run before endpoint logic.
3. The endpoint validates the request server-side:
   - email is required, valid and at most 320 characters;
   - display name is required and at most 200 characters;
   - password is required and at least 8 characters.
4. Validation failure returns `400` `application/problem+json` with field errors.
5. The endpoint trims and normalizes the email, then checks for an existing row with the same `NormalizedEmail`.
6. Duplicate email returns a stable `409 Conflict` problem: `Duplicate email`, without exposing database/index names.
7. A valid password is hashed with `IPasswordHasher<Account>`; only the hash is stored.
8. `Account.CreateUser(...)` creates an active non-platform-admin account and EF Core persists it.
9. The created account is mapped to the same safe user response shape and returned as `201 Created`.

## Backend concepts introduced

- **Query/command separation inside one slice.** Listing users is a query: it reads rows and maps them to safe DTOs. Creating a user is a command: it validates intent, checks uniqueness, hashes a password and writes a row.
- **Server-side validation is authoritative.** Frontend validation helps UX, but the API repeats the rules because HTTP clients can bypass the browser.
- **Email normalization for uniqueness.** User emails are compared through `Account.NormalizeEmail(...)`, so `Admin@Example.com` and `admin@example.com` represent the same account for uniqueness.
- **Safe response mapping.** The entity has `PasswordHash`; the API response type does not. Keeping a small response record makes it harder to accidentally serialize secret material.
- **Race-safe duplicate handling.** The endpoint checks for a duplicate before insert for a friendly response, but still catches `DbUpdateException` so the database unique index remains the final guard if two requests race.

## Important security decisions

- **Both endpoints require platform administrator authorization.** Missing tokens get `401`; valid tokens without `isPlatformAdmin=true` get `403`. The tests cover both outcomes for both endpoints.
- **New users are not platform admins.** `Account.CreateUser(...)` always sets `IsPlatformAdmin = false`; this slice does not introduce role assignment.
- **Password never leaves the command path.** The create request accepts a password, hashes it immediately, stores only the hash and returns no password/hash field. Tests assert response bodies do not contain password or hash text.
- **Duplicate errors are stable and non-leaky.** The API returns a deliberate conflict problem instead of surfacing EF Core exception details or unique-index names.
- **Default to deny.** The endpoint bodies do not implement fallback identity behavior. If authentication or authorization context is absent, ASP.NET Core denies before the handler runs.

## Alternatives deliberately postponed

- **Search, filters and arbitrary sorting.** B006 needs a first visible page for F009, not a full user-directory query language.
- **Edit, delete, password reset and bulk operations.** The slice only proves list and create.
- **Tenant membership and tenant roles.** Created users are platform-level accounts only; tenant membership starts in S07.
- **Invitations and audit events.** Those are later slices, so this task stores the account directly without an invitation workflow or audit log.
- **Repository/service abstractions.** The feature uses `IamDbContext` directly, matching the current modular-monolith guidance and avoiding abstractions for future needs.

## Commands and manual steps to verify

Automated checks used for this slice:

```bash
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests --filter "FullyQualifiedName~UserManagementIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests
dotnet.exe build TenantForge.sln
```

Manual API smoke path for the browser demo readiness:

1. Start PostgreSQL with `docker compose up -d` if it is not already running.
2. Start the API from this backend clone using the Windows SDK via WSL interop:

   ```bash
   dotnet.exe run --project ./src/api/TenantForge.Api/ --urls "http://0.0.0.0:5100"
   ```

3. From WSL, call through the gateway IP from `ip route`:

   ```bash
   GW=<gateway from ip route>
   TOKEN=$(curl -s -X POST "http://$GW:5100/api/auth/login" \
     -H 'Content-Type: application/json' \
     -d '{"email":"admin@tenantforge.local","password":"local-development-password"}' \
     | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')

   curl -i "http://$GW:5100/api/platform/users" -H "Authorization: Bearer $TOKEN"
   curl -i -X POST "http://$GW:5100/api/platform/users" \
     -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' \
     -d '{"email":"new.user@tenantforge.local","displayName":"New User","password":"valid-user-password"}'
   curl -i -X POST "http://$GW:5100/api/platform/users" \
     -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' \
     -d '{"email":"new.user@tenantforge.local","displayName":"Duplicate","password":"valid-user-password"}'
   ```

Expected results: the seeded administrator appears in the list, the new user returns `201 Created` and then appears in the list, and the duplicate email returns `409 Conflict`.

## Review questions

1. Why does the API validate `email`, `displayName` and `password` even if the browser form also validates them?
2. Why does `Account.CreateUser(...)` force `IsPlatformAdmin = false` instead of trusting a request field from the client?
3. The create endpoint checks for duplicates before saving and still catches `DbUpdateException`. What race condition does the catch protect against?
