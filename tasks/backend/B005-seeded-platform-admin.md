---
id: B005
slice: S05
title: Seed and authenticate the platform administrator
agent: backend-mentor
status: planned
depends_on: [B004]
source: tasks/slices/005-seeded-platform-admin.md
---

# Objective

Replace hardcoded credential lookup with an idempotently seeded PostgreSQL
platform administrator while preserving the login contract.

## Scope

- Seed exactly once from safe configuration.
- Authenticate against persisted password material.
- Remove the S01 hardcoded lookup and test repeated startup.

## Acceptance

- Clean startup creates one administrator and repeated startup creates none.
- Existing login/dashboard browser flow still works.
- Secrets are not logged and backend tests/build pass.
