---
id: B012
slice: S11
title: Close platform user access and expose my tenants
agent: backend-mentor
source: tasks/slices/011-platform-tenant-boundaries.md
---

# Objective
Close platform user access and expose my tenants.

## Context
Read tasks/slices/011-platform-tenant-boundaries.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Own UsersFeature.cs, tenant-discovery feature/registration, relevant backend
  integration tests, docs/design/s11-platform-tenant-boundaries.md and
  docs/learning/b012-platform-access-and-my-tenants.md.
- Read docs/design/s09-role-permission-matrix.md for the explicitly superseded
  user-management permission example. Document that contract change before code.
- Make GET and POST /api/platform/users admin-only. tenantId must never grant
  platform authority. Preserve admin success/validation/conflict payloads.
- Implement GET /api/auth/me/tenants exactly as S11; F018 is its consumer.
- Keep existing tenant management and dashboard admin-only. Leave old catalog
  cleanup and role semantics to B013. No new account/member creation workflow.

## Acceptance and demo
- Admin lists/creates accounts. Ordinary Member and Owner receive 403 on both
  operations, with no tenantId, their own tenantId and a foreign tenantId.
  Include a legacy custom role containing IAM.Users.View/Create.
- Denied creation persists no account. Denied reads expose no identity fields.
- Discovery returns only the caller's memberships across two active tenants;
  exclude a third tenant, suspended tenant and deleted membership.
  No memberships returns []; missing/disabled account returns 403, invalid JWT 401.
- Admin discovery does not enumerate tenants where the admin has no membership.
- Browser: demonstrate existing admin Users success and denied direct requests
  as a tenant user; demonstrate the discovery response for F018 integration.
- Existing B009 coverage expecting tenant access to global Users must be updated
  to the explicit S11 decision, not deleted without replacement.

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
