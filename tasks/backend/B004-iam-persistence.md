---
id: B004
slice: S04
title: Add IAM account persistence
agent: backend-mentor
status: planned
depends_on: [B003, F007]
source: tasks/slices/004-iam-account-persistence.md
---

# Objective

Add the PostgreSQL-backed IAM account model and migration while keeping the
existing browser login and dashboard behavior unchanged.

## Scope

- Implement only the S04 entity, mapping, context, migration and test support.
- Do not switch login credential lookup yet.
- Prove constraints and clean-database creation with integration tests.

## Acceptance

- Existing visible behavior still passes as a regression demo.
- Migration and account invariants are tested.
- The learning note makes persistence flow reviewable.
