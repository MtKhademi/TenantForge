---
id: F009
slice: S06
title: Connect user management APIs
agent: ui-engineer
source: tasks/slices/006-user-management.md
---

# Objective

Replace F008 user mocks with persisted users and creation through B006.

## Scope

- Integrate list and create APIs.
- Preserve validation, conflict and loading behavior.
- Remove obsolete user mocks.

## Acceptance

- The seeded administrator appears and a new user persists after refresh.
- Duplicate or invalid input produces the specified feedback.
- Frontend tests and real browser demo pass.
