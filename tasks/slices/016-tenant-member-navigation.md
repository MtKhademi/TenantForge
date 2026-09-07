# S16 — Tenant members and honest navigation scope

## Owner and reported behavior

F023: ui-engineer. A platform admin opens the tenant table, presses «ورود»,
visits roles, invitations or audit, then clicks «کاربران». The selected tenant
disappears and the sidebar changes to platform navigation. The tenant-list
entry also appears active alongside tenant child pages, obscuring the scope.

## Investigation evidence

Source inspected at main c11a454003ad43cef079c23876ce7c129e8ea3db. This is a
code-confirmed route/consumer mismatch; no live browser reproduction was run
while registering the task. Reproduce on the implementation branch after F018,
which will already have removed the ambiguous tenant link to global users.

| Code location | Evidence | Consequence |
|---|---|---|
| src/web/src/pages/TenantsPage.tsx, TenantRow | Enter calls selectTenant(tenant.id) | Opens the chosen tenant |
| src/web/src/features/tenants/TenantScopeContext.tsx | selectTenant navigates to /t/{id}; URL is the selection source | No independent saved active-tenant state is being cleared |
| src/web/src/components/shell/ShellNav.tsx | roles/invitations/audit append scoped suffixes, users has fixed href /users | Users intentionally leaves the tenant URL |
| src/web/src/App.tsx and features/users/userAdapter.ts | /users renders UsersPage and calls /api/platform/users | This is the global account directory, not tenant members |
| src/web/src/components/shell/DashboardShell.tsx and TenantSwitcher.tsx | Tenant scope exists only on /t/... | /users correctly displays platform scope under the current routing model |
| src/web/src/components/shell/ShellNav.tsx | tenants.activePrefixes includes both /platform/tenants and /t/ | Tenant-list entry has aria-current=page even on a different tenant child page |
| src/web/src/pages/TenantScopePage.tsx and features/tenants/tenantMembersAdapter.ts | /t/{id} already calls /api/tenants/{id}/members | Reuse the existing member page and scoped API |

Sidebar anchors currently use native href navigation, which can reload the
document; this is a separate contributor to UI resets, not the cause of the
scope change. Verify it and use router navigation for the affected internal
links. Keeping /users unchanged while remembering a tenant name in storage
would mask the mismatch and make global records look tenant-scoped.

## Product decision and routing contract

Separate platform account management from tenant membership navigation. Retain
the URL as the authoritative active scope, consistent with S11/F018.

| Scope / action | Label | Destination and data |
|---|---|---|
| Tenant table Enter or switcher tenant selection | ورود / tenant name | /t/{id}, existing tenant member page |
| Inside tenant: open people | اعضای مستأجر | /t/{sameId}, GET /api/tenants/{sameId}/members |
| Inside tenant: roles | نقش‌ها | /t/{sameId}/roles, existing scoped API |
| Inside tenant: invitations | دعوت‌ها | /t/{sameId}/invitations, existing scoped API |
| Inside tenant: audit | گزارش فعالیت | /t/{sameId}/audit, existing scoped API |
| Admin explicitly leaves tenant through switcher | رفتن به پلتفرم | Existing platform landing; header shows platform |
| Platform navigation, admin only | کاربران پلتفرم | /users, existing /api/platform/users |
| Platform navigation, admin only | مستأجران | /platform/tenants |

In tenant scope show tenant destinations rather than the global Users item.
Platform directory links are available after the admin explicitly chooses
platform scope. Ordinary members have no platform entry or global user action.
Do not change platform access rules, map tenant users to the global directory,
add tenant account creation, or invent a new member-management screen.

Tenant identity is shown by the existing header/switcher. The current page is
shown by a matching navigation link: /t/{id} activates only members, and each
child activates only its corresponding item. Platform Tenants is not active
on /t/... . At most one visible item per navigation instance has aria-current
of page. Parent scope styling, if retained, must be visually distinct and must
not use aria-current=page. Loading/denial must not expose a previous tenant name.

## Reproduction and acceptance evidence

1. Record the baseline URL, header, sidebar labels/aria-current and Network
   requests while entering tenant A and moving through all four tenant pages.
   In the original behavior, Users changes /t/A/... to /users and requests the
   platform directory. After F018, document its removal and the remaining lack
   of an explicit member destination rather than claiming the old bug persists.
2. After F023, click members from roles, invitations and audit: URL remains
   /t/A, header still identifies A, only members is active, and the member
   request targets A. No /api/platform/users request is made by this action.
3. Repeat for tenant B with distinct members; use reload, Back/Forward, direct
   tenant child URLs and rapid switching with a delayed response. No A members,
   counts, permissions or labels may appear as B's. Respect existing route
   authorization and pagination-independent deep-link handling.
4. Admin explicitly selects platform, opens Users and sees the global directory
   with a platform header and correct active item; returning to an authorized
   tenant via the switcher or Back restores the route-derived scope.
5. An ordinary member can open their existing member view but never requests
   the platform directory through tenant navigation. A foreign/disabled tenant
   and permission-denied child route show the established denied state; direct
   /users is denied for a non-admin under S11. Do not broaden API permissions.
6. Verify expanded/collapsed desktop sidebar and mobile drawer in Persian RTL,
   including keyboard focus, meaningful tooltips and closure after navigation.

## Boundaries and related work

F018 owns platform authorization presentation, my-tenants discovery and removal
of the ambiguous link. F023 adds the explicit existing-member destination and
consistent active-page/scope behavior after that foundation. F019 consumes the
corrected navigation; F021 must retain the distinct Persian labels. F022 later
pages the same member view and selectors without changing this scope contract.

No backend task is required for this diagnosis: the scoped member API exists.
If reproduction exposes a separate API authorization defect, record the exact
request/response as a blocker for its backend owner instead of changing server
policies in this task. Do not add persistent tenant selection, new endpoints,
new product screens, broad layout refactoring or frontend test-policy changes.
