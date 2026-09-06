---
id: B013
slice: S12
title: Unify tenant permission semantics and protect administrators
agent: backend-mentor
source: tasks/slices/012-permission-consistency.md
---

# Objective
Unify tenant permission semantics and protect administrators.

## Context
Read tasks/slices/012-permission-consistency.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design/s09-role-permission-matrix.md and
  docs/design/s10-audit-invitations.md for contracts explicitly revised by S12.
- Own RolesFeature.cs, existing tenant authorization call sites, required
  domain/mapping/migration files, integration tests,
  docs/design/s12-permission-consistency.md and
  docs/learning/b013-tenant-permission-consistency.md.
- Implement the exact four-key catalog and persisted-key migration in S12.
  Preserve existing wire envelopes and assignment/role identities.
- Use one consistent active-account, active-tenant and membership decision for
  member, role, resolved-permission, invitation and audit operations. Change
  authorization only in adjacent features; do not redesign their data flows.
- Replace the IAM.Tenants.Create ownership proxy with explicit Roles.Manage.
  Membership Owner remains authoritative and resolves all four permissions.
- Evaluate last-administrator protection on distinct active accounts after the
  whole proposed mutation, with concurrency protection scoped to the tenant.
- F019 consumes the revised catalog/resolution; no new endpoint is needed.

## Acceptance and demo
- Catalog keys exactly match supported tenant operations. Old/unknown keys in
  new requests return field-specific 400; platform access stays admin-only.
- Verify migration of existing grants with representative pre-migration data;
  no role/member deletion or accidental invitations/audit grants.
- Test a person with two administrator-role assignments, a role shared by
  several administrators, another independent administrator and concurrent
  demotions. Reject operations leaving zero administrators with 409.
- Test each permission's positive/negative API behavior, foreign tenant IDs,
  removed membership, suspended tenant and disabled account using an old JWT.
- Owner permissions and ordinary member resolved permissions match actual
  server decisions. Preserve existing audit records for successful role changes.
- Browser: role create/update/unassign still works; use authenticated requests
  to demonstrate the new catalog and per-permission denial before F019.

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
