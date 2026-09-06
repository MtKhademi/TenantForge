# B010 — Invitations and audit API

## 1. Files changed and why

- `domain/TenantInvitation.cs` stores tenant-scoped pending invitations with normalized email, role, expiry and a hashed token.
- `domain/AuditEvent.cs` stores append-only sensitive action records with actor, action, target and details.
- `infrastructure/*Invitation*`, `infrastructure/*Audit*` and the migration map invitations and audit events into PostgreSQL.
- `features/invitations/InvitationsFeature.cs` implements pending invitation list/create and records `Invitation.Created` events.
- `features/audit/AuditFeature.cs` implements bounded tenant audit queries with optional action/from filters.
- `features/roles/RolesFeature.cs` appends audit events for role create/update/assign/unassign.
- `tests/integration/TenantForge.Api.IntegrationTests/InvitationAuditIntegrationTests.cs` covers success, duplicate, denied, token-safety and tenant-boundary paths.

## 2. Request flow from endpoint to response

### Create invitation

1. The caller sends `POST /api/tenants/{tenantId}/invitations` with a bearer token.
2. ASP.NET Core authenticates the token before endpoint code runs.
3. The feature resolves the account from the `sub` claim and checks tenant membership plus invitation permission.
4. The server validates and normalizes the email and verifies the selected role.
5. The server generates a raw acceptance token for future delivery, hashes it immediately, and persists only the hash.
6. The invitation and an `Invitation.Created` audit event are saved together.
7. The response returns safe invitation fields only.

### Query audit

1. The caller sends `GET /api/tenants/{tenantId}/audit`.
2. The API checks tenant membership and `IAM.Audit.View`.
3. Optional `action` and `fromUtc` filters are validated.
4. Events are returned tenant-scoped, newest first, and bounded to the first page.

## 3. Backend concepts introduced

- Expiring invitation: a pending tenant invitation is valid only until `expiresAtUtc`.
- Token hashing: raw acceptance tokens are secrets; only a one-way hash is stored.
- Tenant-scoped uniqueness: duplicate active invitations are checked per tenant/email.
- Immutable audit record: sensitive commands append events instead of updating existing event rows.
- Actor/target metadata: events capture who did the action and what was affected without storing secrets.

## 4. Important security decisions

- Raw invitation tokens are not returned, logged or stored.
- Duplicate detection only considers active pending invitations in the same tenant.
- Invitation and audit endpoints fail closed with `403` when tenant membership or permission is missing.
- Audit queries are scoped by tenant id before filters are applied.
- Audit details are server-authored prose and must not include passwords, tokens or token hashes.
- No update/delete endpoint exists for audit events.

## 5. Alternatives deliberately postponed

- Production email delivery and templates.
- Invitation acceptance and registration for new accounts.
- Audit export, retention policies and SIEM integration.
- Event bus/outbox infrastructure.
- Billing and subscriptions.

## 6. Commands and manual steps to verify

Commands:

```text
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter InvitationAuditIntegrationTests
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
dotnet.exe build
```

Manual browser handoff for F016:

1. Run the API with the new migration applied.
2. Sign in as a tenant Owner.
3. Open Invitations and create an invitation for a new email.
4. Confirm the pending invitation appears with role, status and expiry, but no token.
5. Open Audit Log and confirm `Invitation.Created` appears with actor, target and time.
6. Make a role change and confirm the related role audit action appears.
7. Sign in as a member without invitation/audit permissions and confirm both pages receive `403` from the API.

## 7. Review questions

1. Why is the raw invitation token generated server-side and stored only as a hash?
2. Why must audit queries filter by tenant before returning any event data?
3. Why is an append-only audit table safer than exposing update/delete endpoints for audit events?
