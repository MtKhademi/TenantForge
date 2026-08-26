---
id: B007
slice: S07
title: Implement tenants and memberships
agent: backend-mentor
source: tasks/slices/007-tenant-membership.md
---

# Objective

Implement tenant creation, first-owner membership and tenant listing required by
the S07 switcher.

## Scope

- Model tenant and membership with required uniqueness constraints.
- Create the first Owner atomically.
- Test authorization, validation and rollback behavior.

## Acceptance

- F011 can create and reload a tenant with its first Owner.
- Invalid or duplicate operations do not leave partial data.
- Backend tests, build and learning note pass.
