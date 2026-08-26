---
id: F002
slice: S01
title: Connect login UI to the development API
agent: ui-engineer
source: tasks/slices/001-hardcoded-admin-login.md
---

# Objective

Replace the S00 login mock with the accepted development login API while
preserving the approved visual design and error states.

## Scope

- Own only frontend API integration, auth state and frontend tests.
- Treat the source slice API contract as fixed.
- Demonstrate success, invalid credentials and unavailable API states.

## Acceptance

- A real development administrator reaches the dashboard through the API.
- Wrong credentials remain on login with accessible feedback.
- Frontend validation and browser checks pass without backend edits.
