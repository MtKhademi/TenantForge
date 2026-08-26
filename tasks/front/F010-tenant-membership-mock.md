---
id: F010
slice: S07
title: Define and mock tenants and memberships
agent: ui-engineer
status: planned
depends_on: [F009]
source: tasks/slices/007-tenant-membership.md
---

# Objective

Build the tenant creation, first-owner assignment and tenant-switcher experience
with mocks and lock the B007 contract.

## Scope

- Implement required screens and UI states only.
- Make tenant context obvious in the shell.
- Do not implement isolation or custom permissions.

## Acceptance

- The mocked S07 demo works on desktop and mobile.
- The contract covers tenant creation and first membership only.
- Frontend checks pass with no backend edits.
