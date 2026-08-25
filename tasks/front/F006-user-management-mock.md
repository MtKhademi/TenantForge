---
id: F006
slice: S06
title: Define and mock user management
agent: ui-engineer
status: planned
depends_on: [F005]
source: tasks/slices/006-user-management.md
---

# Objective

Create the Users list and create-user flow with task-approved mocks, defining
the smallest contract B006 must implement.

## Scope

- Implement list, empty, loading, validation, success and conflict states.
- Keep the page platform-scoped as specified by S06.
- Do not add roles, tenants or invitations.

## Acceptance

- The mocked browser demo covers list and create flows.
- The API contract is explicit and minimal.
- Accessibility, responsive behavior and frontend checks pass.
