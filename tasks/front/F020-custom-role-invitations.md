---
id: F020
slice: S13
title: Handle custom invitation roles and honest pending states
agent: ui-engineer
source: tasks/slices/013-invitation-consistency.md
---

# Objective
Handle custom invitation roles and honest pending states.

## Context
Read tasks/slices/013-invitation-consistency.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design/s13-invitation-consistency.md delivered by B014.
- Own invitationsTypes.ts, invitationsAdapter.ts, InvitationsPage.tsx and
  focused reuse of the existing tenant role read adapter.
- Treat invitation.role as a validated non-empty role-name string, not the
  closed Owner | Viewer union. Historical custom names must remain displayable.
- Offer Owner/Viewer built-in choices and custom roles from the current tenant's
  roles API, deduplicating by name. Keep built-in labels Persian and custom names
  verbatim. Do not change the request to roleId without a new contract.
- Honor Invitations.View/Create independently. A view-only member should not
  fetch creation-only data unnecessarily.
- Display pending status/expiry accurately. Do not imply email was sent.
  Remove the unusable sample acceptance action, or retain only a clearly marked
  non-actionable development explanation; production has no acceptance action.

## Acceptance and demo
- Create a custom role, select it, invite an email, reload and see the same role.
  A valid unfamiliar role name in history does not break the full list.
- Duplicate 409 and role validation 400 show actionable Persian messages.
- Role-list loading/failure disables submission and provides retry; changing
  tenant resets choices and discards stale list/create responses.
- Demonstrate read-only member, denied member, expired row and production build
  without sample acceptance links or false delivery claims.
- No backend edits, mocks, acceptance or email feature.

## Verification
Run from src/web:
- npm run lint
- npm run build

Verify the real API in desktop and mobile browsers; include happy path,
relevant empty/error/401/403 states, reload and tenant switching where relevant.
Check keyboard use, Persian RTL layout and absence of new console errors.
Do not inspect, edit or run frontend test files, per the ui-engineer policy.
No mocks or backend changes. Record environment blockers without waiving checks.


## Lifecycle
Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
this row done, replace its Spec with — and delete exactly this executable Spec
in the same commit; preserve its source slice.
