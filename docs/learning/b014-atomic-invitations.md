# B014 — Atomic, tenant-scoped invitation creation

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/features/invitations/InvitationsFeature.cs`
  The only product file. `POST /api/tenants/{tenantId}/invitations` now wraps the
  duplicate check, role validation, and the invitation + audit insert in one
  database transaction guarded by a transaction-scoped PostgreSQL advisory lock
  keyed by `tenantId:normalizedEmail`. This is what removes the duplicate-active
  race: two concurrent creates for the same tenant + normalized email can no
  longer both pass the precheck.
- `tests/integration/TenantForge.Api.IntegrationTests/InvitationAuditIntegrationTests.cs`
  Adds the S13 acceptance cases: real concurrent duplicate create, trim/case
  normalization, expired-previous re-invite, cross-tenant independence,
  custom-role acceptance, and unknown/foreign custom-role rejection.
- `docs/design/s13-invitation-consistency.md`
  Records the contract F020 consumes: preserved S10 shapes, role validation
  rules, duplicate-active semantics, the concurrency rule, and the no-time-
  dependent-index decision.
- `tasks/TASKS.md`
  Tracks B014 through its lifecycle.

## 2. Request flow from endpoint to response

1. `POST /api/tenants/{tenantId}/invitations` runs through
   `RolesFeature.AuthorizeTenantAccessAsync`, which enforces active account,
   active tenant, membership, and `IAM.Invitations.Create`. Any failure is `403`.
2. Email and role presence/format are validated; failures are `400`.
3. Email is trimmed and lower-cased; role is trimmed.
4. A database transaction is opened and a `pg_advisory_xact_lock` is taken on
   `hashtextextended(tenantId + ":" + normalizedEmail, 0)`. This serializes every
   concurrent create for the same tenant + email across processes.
5. Inside the lock: the role is validated (built-in `Owner`/`Viewer`, or a custom
   role whose `TenantId` matches). Unknown or foreign-tenant roles return `400`.
6. Still inside the lock: the active-duplicate check runs. If an active
   (`Pending`, `expires_at_utc > now`) invitation already exists for that
   normalized email in this tenant, the request returns `409` and the
   transaction is rolled back — no invitation or audit row is written.
7. Otherwise a `TenantInvitation` (with a hashed token) and an
   `Invitation.Created` `AuditEvent` are added and committed in one transaction.
8. The unchanged `201 Created` invitation response is returned.

The `GET` list endpoint is unchanged: it still returns only active invitations.

## 3. Backend concepts introduced

- **Transaction-scoped advisory locking.** `pg_advisory_xact_lock` is a
  cross-process database lock that is released automatically when the
  surrounding transaction ends. Unlike an in-memory lock it works across
  connections and API processes; unlike a unique partial index it does not need
  a time-dependent predicate.
- **Check-then-insert inside the lock.** The duplicate decision and the insert
  must be in the same critical section. A precheck alone (the old code) is a
  classic check-then-act race: two requests both see "no duplicate" and both
  insert.
- **Atomic business write + audit.** The invitation row and its audit event are
  one unit of work, so there is no state where an invitation exists without its
  audit trail (or vice versa).
- **Why no partial unique index.** The product rule "active means
  `expires_at_utc > now()`" cannot be a stable partial-index predicate — `now()`
  in an index predicate would not define "expired at the current instant"
  correctly across time. The advisory lock + in-transaction check is the
  smallest correct tool here.

## 4. Important security decisions

- **Tenant-scoped role validation.** A custom role is accepted only if it belongs
  to the target tenant. A role name from another tenant is rejected with `400`
  and creates nothing, closing the foreign-role leak.
- **Dedicated authorization.** Permission denial is still `403` from the shared
  S12 authorization helper; platform administration is not a tenant bypass.
- **No secrets on the wire.** The token stays hashed (`TokenHash`); responses,
  the list, and audit `details` never include a raw token or hash. Tests assert
  the literal string "token" is absent from the bodies.
- **Only the expected duplicate maps to `409`.** The lock is advisory and the
  insert uses the primary key, so an unexpected persistence failure still throws
  and stays observable rather than being silently swallowed as a conflict.

## 5. Alternatives deliberately postponed

- No schema migration: the advisory-lock approach needs no new index or table.
- No new endpoint; F020 consumes the existing read/create pair.
- No invitation acceptance, registration, email delivery, resend, or revoke.
- No pagination change (that is B015/F022).
- No frontend change; F020 owns the UI consumer.

## 6. Commands and manual steps to verify

Commands (run from the repository root, using `dotnet.exe`):

```text
dotnet.exe build src/api/TenantForge.Api/TenantForge.Api.csproj
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter "FullyQualifiedName~InvitationAuditIntegrationTests"
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
```

Observed results: focused suite 11/11, full suite 78/78, API build 0 warnings /
0 errors.

Manual steps (same-origin, authenticated requests through the existing app/API):

1. Log in as the seeded platform admin and create an owner account and a
   member account, then create a tenant owned by the owner.
2. As the owner, `POST` an invitation with `role: "Viewer"` and confirm `201`.
3. `POST` the same normalized email again and confirm `409`.
4. `POST` an invitation with a custom role created in this tenant and confirm the
   response/list return that custom role name.
5. `POST` with a role name that exists only in another tenant and confirm `400`.
6. As the `Invitations.View`-only member, confirm `GET` works and `POST` is `403`.

## 7. Review questions

1. Why must the duplicate check and the insert run inside the same
   transaction/lock rather than as a precheck followed by an insert?
2. What breaks if a time-dependent partial unique index on
   `expires_at_utc > now()` were used instead of the advisory lock?
3. Why does the foreign-tenant custom-role case return `400` (not `404` or
   `409`), and why must it create no invitation or audit row?
