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
| F023 | S16 | Keep member navigation inside the selected tenant | done | F018 | — |
| F019 | S12 | Render permissions from the server catalog | done | F023, B013 | — |
| F020 | S13 | Handle custom invitation roles and honest pending states | done | F019, B014 | — |
| F021 | S14 | Polish Persian product copy and current documentation | done | F020 | — |
| F022 | S15 | Connect server pagination to tables and selectors | done | F021, B015 | — |
| F024 | S17 | Write a practical Persian user-guide README | done | F022 | — |

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
| B013 | S12 | Unify tenant permission semantics and protect administrators | done | B012, F018 | — |
| B014 | S13 | Make invitation creation atomic and tenant scoped | done | B013, F019 | — |
| B015 | S15 | Add consistent pagination to collection APIs and query filters | done | B014, F021 | — |
| B016 | S18 | Collapse IAM startup behind one registration and one activation seam | done | B015 | — |
| B017 | S19 | Replace persisted IAM GUID identifiers with TSIDs | done | B016 | — |
| B018 | S20 | Extract stable cross-module building blocks | done | B017 | — |
| B019 | S21 | Create a living IAM knowledge base and update gate | done | B018 | — |
| B020 | S22 | Create a living BuildingBlocks knowledge and admission guide | done | B019 | — |
| B021 | S23 | Create the IAM Contract project and move its pagination, login, account and dashboard types | done | B020 | — |
| B022 | S23 | Move users, tenants and tenant-member contract types into the IAM Contract project | done | B021 | — |
| B023 | S23 | Move roles, invitations and audit contract types, then lock the IAM Contract project's exported surface | planned | B022 | [tasks/backend/B023-iam-contract-roles-invitations-audit.md](backend/B023-iam-contract-roles-invitations-audit.md) |
| B024 | S24 | Create a living IAM Contract knowledge and admission guide | planned | B023 | [tasks/backend/B024-iam-contract-knowledge-base.md](backend/B024-iam-contract-knowledge-base.md) |

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

## IAM module composition seam: S18

- [S18 — Keep IAM composition inside the IAM module](slices/018-iam-module-composition-seam.md)
- B016 is the next backend architecture task after B015. It preserves every
  existing HTTP contract and browser flow while reducing the API host to one
  IAM registration call before `Build` and one asynchronous IAM activation
  call after `Build`.
- B016 and F022 have no file ownership overlap and may proceed in parallel.
  Neither task may absorb the other's scope.

## TSID identifiers: S19

- [S19 — Use one safe identifier across database, domain and HTTP boundaries](slices/019-tsid-identifiers.md)
- B017 starts only after B016 is delivered because it changes IAM domain types,
  persistence mappings, startup verification and every IAM HTTP identifier.
- The migration must preserve existing rows and relationships while replacing
  persisted UUID identity columns with PostgreSQL `bigint`. Existing JWTs and
  bookmarked GUID URLs are intentionally invalid after deployment; users must
  sign in again. Registering this task does not implement the migration.


## Cross-module building blocks: S20

- [S20 — Extract stable cross-module building blocks](slices/020-building-blocks.md)
- B018 follows the completed TSID migration because it relocates the proven
  identifier seam as well as `IModuleConfig` into
  `TenantForge.BuildingBlocks`.
- The task preserves all runtime contracts. It introduces an explicit
  API → module → BuildingBlocks dependency rule and a strict admission test so
  the new project cannot become an ownerless `Common` utility bucket.
- Pagination, EF converters and IAM business concerns remain IAM-owned until a
  second real consumer proves a narrower shared abstraction.


## Living IAM knowledge base: S21

- [S21 — Give agents one living IAM knowledge source](slices/021-iam-knowledge-base.md)
- B019 follows B018 so the handbook records the final BuildingBlocks namespaces
  and dependency direction rather than immediately documenting obsolete paths.
- `docs/modules/IAM.md` becomes the read-first source for IAM questions.
  Backend discovery and review must classify every IAM change: update the
  affected handbook sections or state
  `IAM.md impact: none — <specific reason>`.
- This task changes documentation and agent workflow only. Runtime behavior,
  database schema, endpoints and frontend remain unchanged.


## Living BuildingBlocks knowledge: S22

- [S22 — Give BuildingBlocks one living ownership guide](slices/022-building-blocks-knowledge.md)
- B020 follows B019 and extends its read-first/document-impact workflow to
  `docs/building-blocks/README.md`.
- The guide catalogs every exported type, consumer, dependency and contract
  test, and requires admission evidence before shared code can enter the
  project.
- Future BuildingBlocks changes must update the guide or state
  `BuildingBlocks docs impact: none — <specific reason>`; review blocks vague
  or missing impact decisions.


## IAM contract separation: S23

- [S23 — Separate IAM's public contract from its implementation](slices/023-iam-contract-separation.md)
- B021 follows the completed BuildingBlocks knowledge base because it is the
  next architecture task on `src/modules/iam/**`. It creates
  `TenantForge.Modules.Iam.Contract` and moves the pagination, login,
  account and dashboard request/response types into it.
- B022 and B023 continue the same mechanical move in two more batches
  (users/tenants/tenant-members, then roles/invitations/audit), strictly in
  that order — each depends on the previous one because they share the same
  new project and namespace convention. B023 also adds
  `IamContractArchitectureTests`, locking the project's exact 32-type
  exported surface and its zero-outgoing-reference rule.
- This is a pure architecture refactor: no route, JSON shape, status code or
  persisted schema changes across any of the three tasks. The full
  integration suite passing unmodified after each task is the acceptance
  proof, not a new contract.
- Do not run B021/B022/B023 in parallel and do not start implementation as
  part of registering these Specs. B021 is the first candidate once these
  planned rows and Specs are delivered to main.


## Living IAM Contract knowledge base: S24

- [S24 — Give the IAM Contract project one living ownership guide](slices/024-iam-contract-knowledge-base.md)
- B024 follows B023 so `docs/contracts/iam.md` documents the final,
  delivered namespace layout and 32-type roster rather than an in-flight
  one — the same ordering B020 used after B018.
- This task changes documentation and agent workflow only (a new
  `docs/contracts/iam.md`, an `AGENTS.md` "Living IAM Contract knowledge"
  section, and a cross-link from `docs/modules/IAM.md`). Runtime behavior,
  database schema, endpoints and frontend remain unchanged.
- Future changes to `TenantForge.Modules.Iam.Contract` must update the
  guide or state `IAM Contract docs impact: none — <specific reason>`;
  review blocks vague or missing impact decisions, the same rule already in
  force for `docs/modules/IAM.md` and `docs/building-blocks/README.md`.
