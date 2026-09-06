---
id: F019
slice: S12
title: Render permissions from the server catalog
agent: ui-engineer
source: tasks/slices/012-permission-consistency.md
---

# Objective
Render permissions from the server catalog.

## Context
Read tasks/slices/012-permission-consistency.md in full, docs/architecture.md and the current
contracts explicitly referenced below. The source defines the cross-task
contract; this Spec defines this owner's complete implementation boundary.

## Scope and files
- Read docs/design/s12-permission-consistency.md delivered by B013.
- Own permissionCatalog.ts, roleTypes.ts, roleAdapter.ts, RolesPage.tsx and
  permission-aware navigation/action guards.
- Fetch the existing GET /api/permissions/catalog; remove duplicated runtime
  keys/labels as the source of truth. Loading/error must not fall back to grants.
- Render the four tenant permissions and use resolved current-tenant permissions
  for role management, Invitations and Audit navigation. Keep F018 platform gates.
- Keep role names editable only during creation: the existing update endpoint
  accepts permissionKeys only. Do not silently discard a displayed rename edit.
- Refresh roles/resolved permissions after relevant changes and discard stale
  responses when the tenant/session changes. Preserve read-only role viewing.

## Acceptance and demo
- The matrix reflects the real catalog after reload, with Persian grouping.
- Owner creates/assigns an Invitations.View role. The member sees invitations,
  cannot create one and cannot enter audit without its permission.
- A Roles.Manage member can manage roles without global platform access.
- Catalog failure offers retry and never shows a fabricated matrix.
- Show server 400/403/409 feedback, including final-administrator protection,
  without losing the user's unsaved selection unnecessarily.
- No role rename feature, new permission keys, API changes or broad refactor.

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
