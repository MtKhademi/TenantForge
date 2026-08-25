---
id: F003
slice: S02
title: Protect and restore the application shell
agent: ui-engineer
status: planned
depends_on: [F002, B002]
source: tasks/slices/002-authenticated-shell.md
---

# Objective

Connect the shell to current-account and logout APIs so refresh restores the
administrator and invalid authentication returns to login.

## Scope

- Implement protected routing, session bootstrap, logout and visible loading.
- Preserve the existing shell design.
- Demonstrate expired or missing authentication.

## Acceptance

- Refresh, protected navigation and logout match S02.
- Loading and unauthorized states are visible and tested.
- Frontend checks and browser console verification pass.
