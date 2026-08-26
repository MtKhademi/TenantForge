---
id: B010
slice: S10
title: Implement invitations and audit events
agent: backend-mentor
status: planned
depends_on: [B009, F015]
source: tasks/slices/010-audit-and-invitations.md
---

# Objective

Implement safe tenant invitations and immutable sensitive audit events required
by the final starter-kit demo.

## Scope

- Store invitation tokens safely with expiry and tenant scope.
- Record actor, target and relevant metadata for accepted sensitive changes.
- Expose only the S10 pending-invitation and audit query contracts.

## Acceptance

- F016 can create an invitation and display related audit events.
- Raw tokens and secrets never appear in responses or logs.
- Authorization, expiry and immutability paths are integration-tested.
