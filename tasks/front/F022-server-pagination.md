---
id: F022
slice: S15
title: Connect server pagination to tables and selectors
agent: ui-engineer
source: tasks/slices/015-list-pagination.md
---

# Objective

Make all existing business lists navigable with real server pagination,
accurate totals and Persian RTL controls, including later-page selector options.

## Context

Read tasks/slices/015-list-pagination.md, docs/design/s15-list-pagination.md,
docs/design-system.md and docs/architecture.md. B015 must be delivered first;
consume its real seven-endpoint contract with all existing authorization rules.

## Scope and files

- Own pagination types/control(s) under src/web/src and the types/adapters in
  features/users, tenants, roles, invitations and audit, including F018's
  my-tenants adapter. List query types contain pageNumber/pageSize; AuditQuery
  also keeps action/fromUtc. Parse and retain all pagination metadata instead
  of discarding it when extracting an array.
- Update pages/UsersPage.tsx, TenantsPage.tsx, TenantScopePage.tsx, RolesPage.tsx,
  InvitationsPage.tsx and AuditLogPage.tsx. Page roles in their current list/card
  layout; preserve the current design system and role editor behavior.
- Update components/shell/TenantSwitcher.tsx, features/tenants/TenantScopeContext.tsx
  and existing Owner, custom-role and role/member assignment selectors. Keep
  table and selector page states independent; preserve selected off-page IDs.
- Reuse accessible controls: previous/next, current and total pages, visible
  range/filtered total and page size 10/20/50/100, default 20. Handle totalPages=0
  without showing a contradictory page 1 of 0 or a fake positive row range.
- Put table pagination alongside filters in URL query state. Reset page 1 on
  filter, page-size or tenant changes; support reload and Back/Forward. Scope
  request state to account/tenant and discard superseded responses.
- Paginate on the server; no array slicing pretending to page full results.
  Selectors use explicit bounded next/previous or load-more requests, including
  retry/end-of-list states. Never silently stop at 50 or eagerly fetch all pages.
- Preserve tenant deep links and current selection even if absent from the
  loaded page; use existing server-authorized context reads. Keep complete
  resolved permissions separate from the paged role list.
- Refresh matching rows and metadata after mutations without exceeding pageSize.
  Recover a now-out-of-range page with a bounded refetch as specified in S15.
- No backend change, mock data source, new screen, unrelated restyling,
  dependency change or frontend test inspection/editing/execution.

## Acceptance and browser demo

- With B015's reproducible >50-record fixture, every table/list can reach later
  records. Verify next/previous, size changes, partial last page and correct
  filtered total/range, including loading and a truly empty collection.
- Audit paging retains action/fromUtc; changing either resets to page 1 and
  updates the total. Reload and Back/Forward restore the active query state.
- Navigate to a tenant beyond discovery page 1; create a tenant using an Owner
  beyond user page 1; choose a custom invitation role beyond role page 1; assign
  a role to a member beyond member page 1. Preserve selected values while paging.
- Switching tenant/account during an in-flight request never displays previous
  scope data, counts or selection. Permission-gated actions still use complete
  resolved permissions; platform/tenant denial remains understandable in Persian.
- Failed page requests show a retryable error without labeling old rows as new
  data. Show field-level 400 feedback, session expiry (401), forbidden (403),
  no filter matches and expiry-driven out-of-range recovery where applicable.
- Role creation/update and invitation creation refetch appropriate pages/totals;
  unsaved role edits follow the existing discard/save behavior when selection
  changes. Never imply the newly created row matches the current page/filter.
- Controls are keyboard accessible, labeled in Persian, disabled correctly on
  boundaries/loading and usable on desktop/mobile RTL without overflow. Keep
  focus predictable; do not introduce browser console errors.
- Record screenshots and Network request/response evidence for pagination and
  selectors, with no credentials or tokens. Do not commit generated evidence.

## Verification

Run from src/web:

- npm run lint
- npm run build

Use the real API in desktop/mobile browsers for the cases above. Per AGENTS.md,
do not inspect, edit or run frontend tests; browser checks supply the UI evidence.
Record environment blockers without waiving required validation.

## Lifecycle

Status and dependencies live only in tasks/TASKS.md. Follow the owning command,
plan approval and final review gates. After final delivery approval, mark only
F022 done, replace its Spec with — and delete this exact executable Spec in
the same commit. Preserve the permanent S15 source and backend contract.
