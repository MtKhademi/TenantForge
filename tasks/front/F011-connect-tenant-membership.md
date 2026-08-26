---
id: F011
slice: S07
title: Connect tenant and membership APIs
agent: ui-engineer
status: planned
depends_on: [F010, B007]
source: tasks/slices/007-tenant-membership.md
---

# Objective

Replace F010 mocks with persisted tenant and membership behavior from B007.

## Scope

- Integrate creation, owner assignment and switcher data.
- Preserve accepted UI states and contract.
- Remove obsolete tenant mocks.

## Acceptance

- A created tenant survives refresh and appears in the switcher.
- Validation and relevant error behavior are visible.
- Frontend tests and browser verification pass.
