---
id: F010
slice: S08
title: Expose tenant isolation in the UI
agent: ui-engineer
status: planned
depends_on: [F009, B008]
source: tasks/slices/008-tenant-isolation.md
---

# Objective

Show tenant-specific member data and a clear `403` experience backed by B008
server-side isolation.

## Scope

- Integrate tenant switching and isolated member queries.
- Add the specified forbidden state without treating hidden UI as authorization.
- Do not add roles or permission editing.

## Acceptance

- Acme and Globex visibly show different data.
- A non-member receives the designed `403` page.
- Browser and frontend automated checks pass.
