---
id: B014
slice: S13
title: Make invitation creation atomic and tenant scoped
agent: backend-mentor
source: tasks/slices/013-invitation-consistency.md
---

# Objective
Make invitation creation atomic and tenant scoped.

## Context
Read tasks/slices/013-invitation-consistency.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design/s10-audit-invitations.md and
  docs/design/s12-permission-consistency.md.
- Own InvitationsFeature.cs, required invitation mapping/migration files,
  InvitationAuditIntegrationTests.cs,
  docs/design/s13-invitation-consistency.md and
  docs/learning/b014-atomic-invitations.md.
- Preserve existing invitation response shapes, role-name contract and S12
  permission checks. Validate custom roles inside the target tenant.
- Make duplicate-active creation safe across concurrent connections/processes.
  Select and explain the smallest valid PostgreSQL solution in the plan.
- Persist invitation and its audit event atomically; map only the identified
  duplicate race to 409. Support inviting again after expiry.
- F020 consumes the stable, corrected contract. Add no endpoint.

## Acceptance and demo
- Real PostgreSQL concurrency test uses separate requests/DbContexts and
  coordinates overlap: one 201, one 409, one active invitation, one audit event.
- Test trim/case normalization, expired previous invitation, identical emails
  across tenants and an unknown/foreign custom role.
- Preserve dedicated permission denial; no raw token/hash in API or audit data.
- Browser: existing Owner/Viewer create/list works, duplicate displays conflict;
  demonstrate a custom-role response for F020 without changing frontend code.
- No acceptance, registration, email delivery, resend/revoke or audit export.

## Verification
Run from the repository root:
- dotnet build src/api/TenantForge.Api/TenantForge.Api.csproj
- dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj

Use .NET 10 and a working Docker daemon for the PostgreSQL Testcontainers suite.
Record focused security cases and the full suite result. Demonstrate the
existing browser consumer plus authenticated same-origin browser requests for
the revised contract; do not add a temporary test endpoint. Record UI integration
that belongs to the named dependent frontend task separately. Never claim a
blocked browser or database check passed.


## Lifecycle
Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
this row done, replace its Spec with — and delete exactly this executable Spec
in the same commit; preserve its source slice.
