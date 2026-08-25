---
id: F014
slice: S10
title: Connect invitations and audit log APIs
agent: ui-engineer
status: planned
depends_on: [F013, B010]
source: tasks/slices/010-audit-and-invitations.md
---

# Objective

Replace F013 mocks with real invitations and immutable audit events from B010.

## Scope

- Integrate invitation creation, pending list and audit queries.
- Preserve loading, empty, validation and denied states.
- Remove obsolete mocks.

## Acceptance

- A real invitation appears and related audit events are visible.
- Sensitive token material is never rendered or logged.
- Frontend tests, browser demo and console verification pass.
