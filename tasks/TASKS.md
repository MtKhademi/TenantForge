# TenantForge task ledger

This file is the single source of truth for task order, status and dependencies.
Run frontend work from the `front` clone, backend work from the `backend`
clone and use the `main` clone only for coordination and merged truth.

## Status lifecycle

- `planned`: not started; runnable only when every dependency is `done`.
- `in_progress`: implementation is active on its owning task branch.
- `review`: validation is complete and final user approval is pending.
- `done`: delivered through its pull request; dependencies may rely on it.

Readiness and blocking are derived from the dependency column. Never mark a
blocked task runnable by changing its status.

## Spec lifecycle

Every non-done task has one complete executable spec under `tasks/front/` or
`tasks/backend/`. After final approval, the delivery change must:

1. change only the active row to `done`;
2. replace its Spec link with `—`;
3. delete that active executable spec file;
4. preserve the source slice and Git/PR history as the permanent record.

A `done` row must not have a live task spec. A non-done row must have exactly
one valid Spec link.

## Front queue

| ID | Slice | Task | Status | Depends on | Spec |
|---|---|---|---|---|---|
| F001 | S00 | UI foundation and mock login | done | — | — |
| F002 | S01 | Connect development login | done | F001, B001 | — |
| F003 | S02 | Authenticated shell | done | F002, B002 | — |
| F004 | S02 | Refactor Persian RTL interface | done | F003 | — |
| F005 | S02 | Refactor collapsible RTL sidebar | done | F004 | — |
| F006 | S03 | Dashboard summary mock | done | F005 | — |
| F007 | S03 | Connect dashboard summary | done | F006, B003 | — |
| F008 | S06 | User management mock | done | F007 | — |
| F009 | S06 | Connect user management | done | F008, B006 | — |
| F010 | S07 | Tenant membership mock | done | F009 | — |
| F011 | S07 | Connect tenant membership | planned | F010, B007 | [Spec](front/F011-connect-tenant-membership.md) |
| F012 | S08 | Tenant isolation UI | planned | F011, B008 | [Spec](front/F012-tenant-isolation-ui.md) |
| F013 | S09 | Permission matrix mock | planned | F012 | [Spec](front/F013-permission-matrix-mock.md) |
| F014 | S09 | Connect permission matrix | planned | F013, B009 | [Spec](front/F014-connect-permission-matrix.md) |
| F015 | S10 | Audit and invitations mock | planned | F014 | [Spec](front/F015-audit-invitations-mock.md) |
| F016 | S10 | Connect audit and invitations | planned | F015, B010 | [Spec](front/F016-connect-audit-invitations.md) |

## Backend queue

| ID | Slice | Task | Status | Depends on | Spec |
|---|---|---|---|---|---|
| B001 | S01 | Development login API | done | — | — |
| B002 | S02 | Current account API | done | B001, F001 | — |
| B003 | S03 | Dashboard summary API | done | B002, F003 | — |
| B004 | S04 | IAM persistence | done | B003, F007 | — |
| B005 | S05 | Seeded platform admin | done | B004 | — |
| B006 | S06 | User management API | done | B005, F008 | — |
| B007 | S07 | Tenant membership API | done | B006, F010 | — |
| B008 | S08 | Tenant isolation | planned | B007, F011 | [Spec](backend/B008-tenant-isolation.md) |
| B009 | S09 | Role permission API | planned | B008, F013 | [Spec](backend/B009-role-permission-api.md) |
| B010 | S10 | Invitations and audit API | planned | B009, F015 | [Spec](backend/B010-invitations-audit-api.md) |

## Useful parallel starts

- F002 becomes runnable after F001 and B001 are done.
- F002 is runnable now that F001 and B001 are done.
- B003 can run while F004 and F005 establish the Persian RTL shell.
- F006 can begin after F005; B003 may proceed independently once its own
  dependencies are done.
- F008 can begin while B004/B005 establish persistence and seeded login.

When a dependency is pending, report its ID, current status, owning clone and
exact command. Never bypass a dependency merely to keep an agent busy.
