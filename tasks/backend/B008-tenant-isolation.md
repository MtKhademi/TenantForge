---
id: B008
slice: S08
title: Enforce tenant isolation
agent: backend-mentor
source: tasks/slices/008-tenant-isolation.md
---

# Objective

Enforce tenant context and membership so cross-tenant member data cannot be
read, regardless of frontend visibility.

## Scope

- Resolve tenant context explicitly and default to deny.
- Apply isolation to the S08 member query path.
- Test valid membership, missing context and cross-tenant denial.

## Acceptance

- API data differs by tenant and unauthorized access returns `403`.
- Integration tests prove no cross-tenant leak.
- Learning note and backend validation pass.
