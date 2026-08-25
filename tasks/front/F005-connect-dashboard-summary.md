---
id: F005
slice: S03
title: Connect the dashboard summary API
agent: ui-engineer
status: planned
depends_on: [F004, B003]
source: tasks/slices/003-dashboard-summary.md
---

# Objective

Replace F004 dashboard mocks with the real B003 API without changing the
accepted user experience.

## Scope

- Integrate data fetching, loading, retry and failure behavior.
- Keep the accepted contract unchanged.
- Remove obsolete summary mocks.

## Acceptance

- Dashboard values come from the running API.
- Loading and unavailable-API states work in the browser.
- Frontend build, tests and console verification pass.
