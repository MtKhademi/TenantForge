---
id: F018
slice: S11
title: Navigate platform and tenant scopes correctly
agent: ui-engineer
source: tasks/slices/011-platform-tenant-boundaries.md
---

# Objective
Navigate platform and tenant scopes correctly.

## Context
Read tasks/slices/011-platform-tenant-boundaries.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design/s11-platform-tenant-boundaries.md delivered by B012.
- Own App.tsx, auth landing behavior, TenantScopeContext, tenant adapters/types,
  TenantSwitcher, ShellNav and the existing tenant entry/denied states.
- Platform admins retain /dashboard, /users and /platform/tenants.
  Non-admin accounts never use the platform list to populate the switcher.
- Consume GET /api/auth/me/tenants. Keep discovery DTOs separate from the richer
  platform tenant summary; do not invent memberCount or timestamps.
- After ordinary login choose a valid tenant, or show an in-shell membership
  chooser/empty state. Do not land on the platform dashboard and trigger a 403.
- Preserve an authorized tenant deep link. Invalid/foreign tenant links show
  denial without briefly rendering the last tenant's content.
- Gate platform links/routes using isPlatformAdmin, not tenant permission keys.
  Remove the tenant Users link to the global account directory.
- Correct membership role typing to Owner | Member and localize built-in labels.

## Acceptance and demo
- Demo admin, single-tenant member, two-tenant member and no-membership account.
  Admin still lists/creates users and manages tenants.
- Ordinary login and reload cause no routine platform API requests/403s.
- Switching tenants clears prior members and permissions while loading; a
  delayed old request or logout/login cannot restore another scope's data.
- Direct /users or platform dashboard navigation as a member is denied.
  Navigation remains presentation only; server denial is independently verified.
- Keep existing URLs wherever possible; no new dashboard design or mocks.

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
