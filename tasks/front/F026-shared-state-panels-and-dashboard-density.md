---
id: F026
slice: S25
title: Extract a shared state panel and give dashboard cards semantic weight
agent: ui-engineer
source: tasks/slices/025-visual-polish-pass.md
---

# Objective

Replace the page-level error/empty panel markup that is currently
duplicated across eight page files with one shared `StatePanel` component,
and give the dashboard's three summary cards semantic color weight instead
of one flat neutral treatment. No copy, behavior, route or API-contract
change — this is a presentation refactor plus a small, token-only color
change.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` (including
the two rules F025 added — this task depends on F025 being `done` first).
Read the complete `tasks/slices/025-visual-polish-pass.md` — it is the
authoritative contract for this task.

Before editing, run this to find every current occurrence and confirm the
exact markup you are replacing (file paths may have shifted since this
Spec was written):

```bash
grep -rn 'rounded-xl border border-destructive/40 bg-destructive/10' src/web/src/pages
```

Read each matched block in its file before touching it — every one must
keep its exact current icon, title, description and retry-button wiring;
only the wrapping markup becomes the shared component.

# Scope

1. Create `src/web/src/components/ui/StatePanel.tsx`:
   - Props: `icon` (`ReactNode`), `title` (`string`), `description`
     (`ReactNode`), `action` (optional `ReactNode` — the retry button
     slot), `tone` (`'destructive'` for now — accept the prop but only
     implement the one tone this task actually uses; do not build unused
     tones speculatively).
   - Rendered shape matches today's destructive panels exactly:
     `rounded-xl border border-destructive/40 bg-destructive/10 p-{5,6}
     shadow-soft`, `role="alert"`, icon + title/description block, action
     slot rendered after the description when provided.
   - Accept `className` for the one or two call sites that currently use
     `p-5` vs `p-6` or a slightly different wrapper (`aria-label` on the
     surrounding `<section>`/`<div>` stays on the caller, not inside
     `StatePanel`, since that varies per page).
2. Replace the matched blocks in exactly these files with `StatePanel`,
   preserving each one's current icon, exact Persian copy, and retry
   handler:
   - `src/web/src/pages/DashboardPage.tsx`
   - `src/web/src/pages/UsersPage.tsx`
   - `src/web/src/pages/TenantsPage.tsx`
   - `src/web/src/pages/TenantHome.tsx`
   - `src/web/src/pages/TenantScopePage.tsx`
   - `src/web/src/pages/RolesPage.tsx`
   - `src/web/src/pages/InvitationsPage.tsx`
   - `src/web/src/pages/AuditLogPage.tsx`
   Do not touch the smaller, inline one-line form-field error strings in
   `RolesPage.tsx`/`InvitationsPage.tsx` forms (for example
   `<p className="text-sm text-destructive" role="alert">...`) — those
   are a different pattern, explicitly out of scope per the slice.
3. In `DashboardPage.tsx`'s `SummaryCard`/its three call sites, change the
   icon-well background/foreground from the current uniform
   `bg-muted text-muted-foreground` to:
   - محیط (`environment`): unchanged (`bg-muted text-muted-foreground`) —
     purely informational, no status meaning.
   - وضعیت API (`apiStatus`): `bg-success/10 text-success` when
     `summary.apiStatus === 'Healthy'`, otherwise keep
     `bg-muted text-muted-foreground` (mirror the existing dot-indicator
     condition already in this card — do not invent a new condition).
   - مدیران پلتفرم (`platformAdminCount`): `bg-primary/10 text-primary`.
   Give the three cards a touch more presence (slightly increased padding
   and/or a `min-h` utility) so they read as substantial, not as three
   small chips — do not add a fourth card, a chart, or any metric not in
   `DashboardSummary`.

# Non-goals

- No new design token.
- No new API call, metric or invented content anywhere on any touched
  page.
- No change to loading-skeleton markup beyond what naturally follows from
  the card padding/min-height change (if the skeleton's placeholder shapes
  need the same size bump for the no-layout-shift contract to still hold,
  update them together — do not let the loaded and loading states drift
  apart in size).

# Acceptance

- `StatePanel` exists, is used by all eight listed pages for their
  page-level error panel, and no page still has the old inline markup for
  that panel.
- Every replaced panel's rendered icon, title text, description text and
  retry button behavior is pixel/behavior-identical to before — verified
  in the browser, not just by reading the diff.
- The dashboard's three summary cards show the three described tones;
  toggling between light and dark theme keeps sufficient contrast (reuse
  of existing tokens should make this automatic — confirm it is).
- The dashboard's no-layout-shift contract (`SummarySkeleton` matching the
  loaded card footprint) still holds after any size change.
- No behavior, route, copy or API contract changed on any of the eight
  pages — only the error-panel's implementation and the dashboard cards'
  color/size.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- For at least three of the eight pages (dashboard, one platform list,
  one tenant-scoped page), force the error state (e.g. temporarily break
  the adapter's URL, or use existing dev tooling) and confirm the panel
  still shows the exact same icon/copy/retry behavior as before, now
  rendered through `StatePanel`.
- Dashboard happy path: confirm the three cards show the correct tone
  (success only when `apiStatus` is `Healthy`) and that the loading
  skeleton still causes zero layout shift when data arrives.
- No new browser console error.

# Lifecycle

Add row `F026` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F026-shared-state-panels-and-dashboard-density.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/025-visual-polish-pass.md` is the permanent record
and is never deleted.
