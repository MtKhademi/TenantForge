---
id: F015
slice: S10
title: Define and mock invitations and audit log
agent: ui-engineer
status: planned
depends_on: [F014]
source: tasks/slices/010-audit-and-invitations.md
---

# Objective

Build the pending-invitation and audit-log experience with mocks and lock the
smallest contract for B010.

## Scope

- Implement invite form, pending list, audit table and their states.
- Keep sensitive values out of the UI and mock data.
- Do not add email delivery infrastructure beyond S10.

## Acceptance

- The mocked S10 browser demo is complete and responsive.
- The contract identifies safe invitation and audit fields.
- Frontend validation passes without backend changes.
