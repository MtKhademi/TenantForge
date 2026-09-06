---
id: F021
slice: S14
title: Polish Persian product copy and current documentation
agent: ui-engineer
source: tasks/slices/014-product-copy-and-current-docs.md
---

# Objective
Polish Persian product copy and current documentation.

## Context
Read tasks/slices/014-product-copy-and-current-docs.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design-system.md, README.md, src/web/README.md and
  docs/three-clone-workflow.md as current documents to reconcile.
- Own user-facing copy in existing src/web pages/shell and src/web/README.md.
  Sole shared-file owner for README.md, docs/design-system.md and
  docs/three-clone-workflow.md in this task.
- Remove task IDs, API explanations and implementation progress from product
  screens; preserve concise useful loading/empty/error explanations.
- Hide development credentials outside import.meta.env.DEV. Preserve existing
  development setup instructions in documentation.
- Localize built-in role/status labels; preserve email/identifier readability
  and user-entered custom names. Keep the current visual system.
- Update current docs to actual implemented stack, Persian RTL/right sidebar,
  /health and real setup commands. Link the ledger for live status.
- Clarify invitation acceptance/email limitations. Preserve historical source
  slices and learning notes; do not change agent policies or command semantics.

## Acceptance and demo
- Review login, dashboard, users, tenants, members, roles, invitations and audit
  in desktop/mobile, including an empty/denied state.
- Production UI contains no task IDs, sample credentials, fake acceptance action
  or claim of email delivery. Development instructions remain available.
- Documented startup reaches /health and Persian login. Record actual commands
  and prerequisites, not assumed successful execution.
- No dependency changes, broad component refactor, restyling or frontend tests.
  The deferred QA ownership decision in S14 remains explicit.

## Verification
Run from src/web:
- npm run lint
- npm run build

Verify the real API in desktop and mobile browsers; include happy path,
relevant empty/error/401/403 states, reload and tenant switching where relevant.
Check keyboard use, Persian RTL layout and absence of new console errors.
Do not inspect, edit or run frontend test files, per the ui-engineer policy.
No mocks or backend changes. Record environment blockers without waiving checks.


## Lifecycle
Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
this row done, replace its Spec with — and delete exactly this executable Spec
in the same commit; preserve its source slice.
