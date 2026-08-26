---
id: F013
slice: S09
title: Define and mock the role permission matrix
agent: ui-engineer
status: planned
depends_on: [F012]
source: tasks/slices/009-role-permission-matrix.md
---

# Objective

Build role creation, grouped permission selection and member assignment with
mocks, defining the exact contract for B009.

## Scope

- Implement role list, editor, matrix and denied UI states.
- Keep permission keys and grouping aligned with S09.
- Do not add platform impersonation or direct user grants.

## Acceptance

- The mocked permission workflow is complete and accessible.
- The backend contract and security expectations are explicit.
- Frontend checks pass without backend changes.
