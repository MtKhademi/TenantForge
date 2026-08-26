---
id: B009
slice: S09
title: Implement tenant roles and permissions
agent: backend-mentor
source: tasks/slices/009-role-permission-matrix.md
---

# Objective

Implement tenant role creation, permission assignment and member-role assignment
for the accepted S09 matrix contract.

## Scope

- Keep permission keys explicit and tenant-scoped.
- Enforce permissions server-side and protect the last Owner invariant.
- Test allowed, denied and cross-tenant behavior.

## Acceptance

- F014 can persist and reload a custom role assignment.
- Denied operations remain denied even when called directly.
- Backend tests, build and learning note pass.
