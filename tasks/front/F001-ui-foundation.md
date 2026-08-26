---
id: F001
slice: S00
title: Build the UI foundation and mock login
agent: ui-engineer
status: done
depends_on: []
source: tasks/slices/000-ui-foundation.md
---

# Objective

Create the first browser-visible TenantForge experience: polished login, mock
administrator entry and responsive dashboard shell.

## Scope

- Implement only `src/web/**` and frontend tests.
- Follow the complete source slice, including states, RTL/LTR and responsive QA.
- Keep authentication mocked exactly as permitted by S00.

## Acceptance

- The S00 desktop and mobile demo works in a real browser.
- Frontend build, lint and tests pass with no console errors.
- No backend project or future dashboard feature is added.
