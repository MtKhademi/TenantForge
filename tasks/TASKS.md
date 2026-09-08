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
| F011 | S07 | Connect tenant membership | done | F010, B007 | — |
| F012 | S08 | Tenant isolation UI | done | F011, B008 | — |
| F013 | S09 | Permission matrix mock | done | F012 | — |
| F014 | S09 | Connect permission matrix | done | F013, B009 | — |
| F015 | S10 | Audit and invitations mock | done | F014 | — |
| F016 | S10 | Connect audit and invitations | done | F015, B010 | — |
| F017 | S08 | Verify non-admin tenant isolation in browser | done | F012, B011 | — |
| F018 | S11 | Navigate platform and tenant scopes correctly | done | F017, B012 | — |
| F023 | S16 | Keep member navigation inside the selected tenant | planned | F018 | [Spec](front/F023-tenant-member-navigation.md) |
| F019 | S12 | Render permissions from the server catalog | planned | F023, B013 | [Spec](front/F019-server-permission-matrix.md) |
| F020 | S13 | Handle custom invitation roles and honest pending states | planned | F019, B014 | [Spec](front/F020-custom-role-invitations.md) |
| F021 | S14 | Polish Persian product copy and current documentation | planned | F020 | [Spec](front/F021-product-copy-and-docs.md) |
| F022 | S15 | Connect server pagination to tables and selectors | planned | F021, B015 | [Spec](front/F022-server-pagination.md) |
| F024 | S17 | Write a practical Persian user-guide README | planned | F022 | [Spec](front/F024-persian-user-guide.md) |

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
| B008 | S08 | Tenant isolation | done | B007, F011 | — |
| B009 | S09 | Role permission API | done | B008, F013 | — |
| B010 | S10 | Invitations and audit API | done | B009, F015 | — |
| B011 | S02 | Current account for all authenticated users | done | B008 | — |
| B012 | S11 | Close platform user access and expose my tenants | done | B009, B011, F017 | — |
| B013 | S12 | Unify tenant permission semantics and protect administrators | planned | B012, F018 | [Spec](backend/B013-tenant-permission-consistency.md) |
| B014 | S13 | Make invitation creation atomic and tenant scoped | planned | B013, F019 | [Spec](backend/B014-atomic-invitations.md) |
| B015 | S15 | Add consistent pagination to collection APIs and query filters | planned | B014, F021 | [Spec](backend/B015-list-pagination.md) |

## Cleanup batch: S11–S14

Sources preserve the decisions and demos after executable Specs are removed:

- [S11 — Platform and tenant access boundaries](slices/011-platform-tenant-boundaries.md)
- [S12 — Consistent tenant permissions](slices/012-permission-consistency.md)
- [S13 — Reliable existing invitations](slices/013-invitation-consistency.md)
- [S14 — Clear Persian UI and accurate current documentation](slices/014-product-copy-and-current-docs.md)

Execution order, including the S16 navigation fix, is
B012 → F018 → F023 → B013 → F019 → B014 → F020 → F021.
The dependencies deliberately require each contract change and its UI consumer
to be delivered before the next cleanup pair begins. Do not run these tasks in
parallel or start implementation as part of registering this batch. B012 is the
first candidate once these planned rows and Specs are delivered to main; derive
subsequent readiness from the table, not this explanatory text.

Invitation acceptance/email delivery, broad architecture refactoring and
frontend test repair are outside this batch. S14 records the existing frontend
test ownership restriction; these Specs do not change agent permissions.

When a dependency is pending, report its ID, current status, owning clone and
exact command. Never bypass a dependency merely to keep an agent busy.

## Pagination: S15

- [S15 — Server pagination for lists and their UI consumers](slices/015-list-pagination.md)
- Continue after the cleanup batch: F021 → B015 → F022.
- B015 adds pagination query parameters and response metadata to the seven
  business collection reads; F022 connects tables, role lists and selectors.
- Both tasks remain planned. Registering these Specs does not implement them
  or change the status, dependencies or scope of existing tasks.

## Tenant member navigation: S16

- [S16 — Tenant members and honest navigation scope](slices/016-tenant-member-navigation.md)
- F023 follows F018 and precedes F019 despite its later numeric ID. It completes
  the tenant member destination after F018 removes the ambiguous global Users
  link. B013 retains its backend dependencies; do not run overlapping work in
  parallel. Read execution readiness from the ledger.
- F019 now requires F023 as well as B013. All other existing dependencies and
  task statuses are unchanged; pagination remains downstream of this fix.

## Practical user guide: S17

- [S17 — Learn to use TenantForge through one working example](slices/017-persian-user-guide.md)
- F024 follows F022 so the guide documents the delivered navigation, Persian
  labels, permission behavior and pagination. F023 and F021 are transitive
  prerequisites; existing task dependencies and statuses remain unchanged.
- The deliverable is docs/user-guide/README.md in Persian, linked prominently
  from the root README. This row registers the writing task, not a completed guide.
