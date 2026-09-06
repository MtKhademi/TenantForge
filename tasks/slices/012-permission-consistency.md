# S12 — Consistent tenant permissions

## Executable tasks and owner
- B013: backend-mentor; authorization semantics, catalog and migration.
- F019: ui-engineer; consume the catalog and resolved permissions.

## Context and contract decision
S09 uses IAM.Tenants.Create as effective ownership, while invitations/audit
recognize membership Owner separately. Owner counting counts assignments and
can overlook the effect of updating a role shared by several people.
The S10 omission of invitation/audit keys from the matrix was intentional;
this slice explicitly ends that deferral now those screens exist.

## Permission contract
The tenant catalog contains IAM.Roles.Manage (write), IAM.Invitations.View
(read), IAM.Invitations.Create (write), and IAM.Audit.View (read), in Persian
groups using the existing GET /api/permissions/catalog response shape.
Membership Owner grants all tenant permissions. A member with Roles.Manage may
manage roles, but does not implicitly receive invitation or audit permissions.
Tenant membership remains required; platform administration is not a bypass.
Existing active-member role reads remain available to support invitation role
selection. Writes require Owner membership or Roles.Manage.

The five former dashboard/users/tenants keys no longer belong to the tenant
catalog or resolved permissions. Migrate persisted IAM.Tenants.Create grants
to IAM.Roles.Manage, remove other obsolete keys and preserve role identities
and assignments. This preserves delegated role management without global
platform access. New writes containing obsolete/unknown keys return 400.
Do not manufacture invitation/audit grants for ordinary custom roles.

## Visible outcome and demo
Owner creates a role containing Invitations.View only and assigns it to a
member. That member sees invitations but cannot create them or read audit.
A separate Roles.Manage member can edit roles without gaining platform access.
The role editor reloads the same server-owned catalog and persisted grants.
Removing the final effective role administrator is rejected with 409.

## Security invariant and verification
An effective role administrator is a distinct active account with membership
Owner or an assigned role containing Roles.Manage in an active tenant.
Evaluate the proposed final state of a role update/unassignment across all
affected members, and protect concurrent mutations from removing the last one.
A second assignment to the same person is not a second administrator.
No missing/disabled account, removed membership or suspended tenant can pass
member/role/permission/invitation/audit authorization with an old valid JWT.
Retain non-leaking 403 and authorized-resource 404 semantics.

Use real PostgreSQL integration tests for the migration, final-state invariant,
concurrent demotions, isolated tenants and permission-specific allowed/denied
requests. No new endpoints or generalized authorization framework.
B013 owns docs/design/s12-permission-consistency.md and its learning note.
F019 is the immediately dependent catalog consumer.
