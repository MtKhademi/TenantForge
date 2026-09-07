---
id: F023
slice: S16
title: Keep member navigation inside the selected tenant
agent: ui-engineer
source: tasks/slices/016-tenant-member-navigation.md
---

# Objective

From a tenant's roles, invitations or audit, users can open «اعضای مستأجر»
without leaving that tenant. The header and active navigation item accurately
distinguish tenant membership from platform account management.

## Context and investigation

Read tasks/slices/016-tenant-member-navigation.md in full, the S11 source,
docs/design/s11-platform-tenant-boundaries.md delivered by B012, and
docs/design-system.md. F018 must be delivered before this task.

The registered investigation found a fixed /users link calling the global
/api/platform/users API; active tenant selection is derived from /t/{id}.
The existing /t/{id} page already reads /api/tenants/{id}/members. A broad /t/
active prefix also marks the platform tenant-list link as current on child
pages. This is a navigation/label mismatch, not evidence that membership was
deleted or that tenant data should be supplied by the global users endpoint.

Reproduce the original sequence on the post-F018 branch with a real API and
record which parts F018 has already resolved. Do not restore its removed global
Users link merely to reproduce the report. Retain the independent F023 outcome:
an explicit member destination and unambiguous page/scope state.

## Scope and files

- Own src/web/src/components/shell/ShellNav.tsx, TenantSwitcher.tsx and
  DashboardShell.tsx; touch App.tsx, features/tenants/TenantScopeContext.tsx and
  pages/TenantScopePage.tsx or UsersPage.tsx only as required for routing/labels.
- Add «اعضای مستأجر» to tenant navigation, linked to the existing /t/{tenantId}
  member page. Keep the same tenant ID on members, roles, invitations and audit.
  Preserve the API's actual read policy; do not gate this member view with a
  platform-only permission or a new made-up permission.
- Keep global /users as «کاربران پلتفرم» in admin platform navigation only.
  Within tenant navigation, require the explicit platform switcher action to
  enter platform scope before exposing platform directory links. Keep F018's
  non-admin route guard and membership discovery behavior.
- Derive scope from matched routes consistently across nav/header/switcher.
  Keep current URLs and reuse the member view. No localStorage/sessionStorage
  active-tenant workaround and no global users table under a tenant header.
- Use exact member-page matching and correct child-route matching. Remove the
  broad platform Tenants current-page match for /t/ paths. Only the actual page
  gets aria-current=page in each visible navigation instance.
- Use React Router navigation for affected internal links, keeping normal link
  semantics and keyboard/new-tab behavior. Prevent unnecessary document reloads;
  do not refactor the whole shell layout to fix this task.
- Preserve empty/loading/denied states and discard stale tenant/account state
  under rapid navigation, reload, logout and a subsequent login.
- No backend, permission-policy, dependency or frontend test changes. No mocks
  or new member CRUD capability. Pagination remains assigned to B015/F022.

## Acceptance and browser demo

- Follow the six-step evidence matrix in S16 with distinct tenants A and B,
  a platform admin who can enter them and an ordinary tenant member.
- Enter A through the tenants table, visit roles/invitations/audit, then members.
  A remains in the URL/header, only the members link is active, A's members are
  displayed and Network shows the A member read with no platform users read.
- For each tenant child route, the matching child alone is current. The global
  Tenants entry never masquerades as the current page inside a tenant.
- Tenant B, reload, direct child URL, Back/Forward and delayed response cases
  preserve the URL-derived scope without showing stale A content or permissions.
- Explicit admin transition to platform shows platform scope and permits global
  Users. Non-admin tenant navigation never calls the platform user directory;
  direct /users remains denied. Existing foreign-tenant/permission denial works.
- Expanded/collapsed sidebar and mobile drawer have consistent Persian labels,
  correct active indicators, usable tooltips/focus and no new console errors.
- Capture baseline and fixed route/Network/visual evidence without tokens or
  credentials. Do not commit generated browser artifacts. If a backend defect
  blocks verification, report its exact scenario without bypassing authorization.

## Verification

Run from src/web:

- npm run lint
- npm run build

Use real browser verification against the API for the acceptance matrix. Do not
inspect, edit or run frontend tests per AGENTS.md. Record environment blockers
without claiming blocked checks passed. Task registration itself performs no
application implementation or browser validation.

## Lifecycle

Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
F023 done, replace its Spec with — and delete this exact executable Spec in
the same commit. Preserve the permanent S16 investigation and contract.
