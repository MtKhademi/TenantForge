# TenantForge executable roadmap

TenantForge uses two independent task queues. Run front tasks from the `front`
clone and backend tasks from the `backend` clone. The `main` clone is for
coordination and merged truth.

Task-file `status` is authoritative. This roadmap intentionally has no mutable
status column, so simultaneous front and backend pull requests do not conflict.

## Front queue

| ID | Slice | Task | Depends on |
|---|---|---|---|
| F001 | S00 | [UI foundation and mock login](front/F001-ui-foundation.md) | — |
| F002 | S01 | [Connect development login](front/F002-connect-development-login.md) | F001, B001 |
| F003 | S02 | [Authenticated shell](front/F003-authenticated-shell.md) | F002, B002 |
| F004 | S03 | [Dashboard summary mock](front/F004-dashboard-summary-mock.md) | F003 |
| F005 | S03 | [Connect dashboard summary](front/F005-connect-dashboard-summary.md) | F004, B003 |
| F006 | S06 | [User management mock](front/F006-user-management-mock.md) | F005 |
| F007 | S06 | [Connect user management](front/F007-connect-user-management.md) | F006, B006 |
| F008 | S07 | [Tenant membership mock](front/F008-tenant-membership-mock.md) | F007 |
| F009 | S07 | [Connect tenant membership](front/F009-connect-tenant-membership.md) | F008, B007 |
| F010 | S08 | [Tenant isolation UI](front/F010-tenant-isolation-ui.md) | F009, B008 |
| F011 | S09 | [Permission matrix mock](front/F011-permission-matrix-mock.md) | F010 |
| F012 | S09 | [Connect permission matrix](front/F012-connect-permission-matrix.md) | F011, B009 |
| F013 | S10 | [Audit and invitations mock](front/F013-audit-invitations-mock.md) | F012 |
| F014 | S10 | [Connect audit and invitations](front/F014-connect-audit-invitations.md) | F013, B010 |

## Backend queue

| ID | Slice | Task | Depends on |
|---|---|---|---|
| B001 | S01 | [Development login API](backend/B001-development-login-api.md) | — |
| B002 | S02 | [Current account API](backend/B002-current-account-api.md) | B001, F001 |
| B003 | S03 | [Dashboard summary API](backend/B003-dashboard-summary-api.md) | B002, F003 |
| B004 | S04 | [IAM persistence](backend/B004-iam-persistence.md) | B003, F005 |
| B005 | S05 | [Seeded platform admin](backend/B005-seeded-platform-admin.md) | B004 |
| B006 | S06 | [User management API](backend/B006-user-management-api.md) | B005, F006 |
| B007 | S07 | [Tenant membership API](backend/B007-tenant-membership-api.md) | B006, F008 |
| B008 | S08 | [Tenant isolation](backend/B008-tenant-isolation.md) | B007, F009 |
| B009 | S09 | [Role permission API](backend/B009-role-permission-api.md) | B008, F011 |
| B010 | S10 | [Invitations and audit API](backend/B010-invitations-audit-api.md) | B009, F013 |

## Useful parallel starts

- F001 and B001 can start together because they own disjoint application paths
  and share the fixed S01 contract.
- After both merge, F002 and B002 can run together.
- F004 and B003 can run together after F003 because the S03 contract is fixed.
- F006 can begin while B004/B005 establish persistence and seeded login.

When a dependency is pending, the owning command reports which clone and command
must complete it. Never bypass a dependency merely to keep an agent busy.

## Completion rule

A task becomes `done` only in its own delivery PR after automated validation,
browser or API evidence, review and final user approval. Completed task and slice
specification files remain public as learning material.
