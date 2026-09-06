---
id: F017
slice: S08
title: Verify non-admin tenant isolation in the browser
agent: ui-engineer
source: tasks/slices/008-tenant-isolation.md
---

# Objective

Close the S08 visible outcome blocked during F012: once B011 makes
`GET /api/auth/me` work for every authenticated user, demonstrate in a real
browser that a non-admin tenant member holds a session, sees their own tenant's
member list, and is denied access to a tenant they do not belong to.

## Scope

- No new UI code is expected if B011 fully unblocks the session; verify the
  existing F012 states (member list, designed 403, invalid selection, retry)
  render for a non-admin.
- Browser demo: sign in as a tenant member (not platform admin); open a tenant
  they belong to and see its members; open a tenant they do not belong to and
  see the designed 403 page with recovery to the platform view.
- Confirm the tenant switcher and header scope badge behave correctly for a
  non-admin (neutral scope label when the tenant is not in their platform list).
- Capture desktop (1440×900) and mobile (390×844) screenshots; check console
  errors.

## Acceptance

- A non-admin member can log in, stay authenticated across refresh, and view
  their own tenant's real member list in the browser.
- The same non-admin receives the designed 403 page for a tenant they are not a
  member of, with recovery to the platform view.
- Frontend build, lint and the browser verification pass.
