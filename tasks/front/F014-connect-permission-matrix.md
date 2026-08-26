---
id: F014
slice: S09
title: Connect role and permission APIs
agent: ui-engineer
source: tasks/slices/009-role-permission-matrix.md
---

# Objective

Replace F013 mocks with persisted tenant roles, permissions and assignments from
B009.

## Scope

- Integrate all accepted S09 endpoints without changing the contract.
- Update navigation visibility while retaining server-enforced denial.
- Remove obsolete permission mocks.

## Acceptance

- Role creation and assignment survive refresh.
- Allowed navigation and denied operations match the server response.
- Frontend tests and browser demo pass.
