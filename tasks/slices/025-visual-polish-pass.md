# S25 — Visual polish pass: brand rhythm, semantic weight, shared states

## Outcome

TenantForge's existing design system (`docs/design-system.md`,
`tenantforge-ui-system`) is correct and stays in force unchanged — this
slice does not replace it or introduce a new skill. It closes a concrete
gap between the documented "calm, capable B2B SaaS" direction and today's
implementation: real screens repeat the brand mark redundantly, use
identical, colorless treatment for every summary card regardless of
meaning, duplicate the same error/empty panel markup across eight page
files, and — on the login page specifically — make a mobile visitor
scroll past a full-height hero before reaching the sign-in form.

This was diagnosed by running the real application in a browser (Playwright,
against a temporary, fully-reverted mock-fetch harness — no code from that
session remains) and screenshotting the login, dashboard, users, tenant
member, roles and mobile-drawer screens. Every finding below cites the
exact file and line it came from; a task should re-verify against current
code, not trust this document blindly if the file has since changed.

## Findings and the exact rule each one becomes

| Finding | Evidence | Rule added to `docs/design-system.md` |
| --- | --- | --- |
| "TenantForge" appears three times in view at once: the sidebar `BrandRow` (`ShellNav.tsx` ~231-236), the header's small caption (`DashboardShell.tsx` line 154), and the login hero repeats it twice in the same panel (`LoginPage.tsx` lines 85 and 88) | Dashboard/login screenshots | **One brand mark per screen.** A screen shows the TenantForge mark (icon + wordmark) exactly once. A secondary header caption never repeats the wordmark; use the scope/tenant indicator only. |
| The theme toggle button in `DashboardShell.tsx` (lines 170-178) is a bespoke `<button>` with its own border/height classes, inconsistent with the `SecondaryButton` primitive used for every other header icon button (menu, sidebar collapse, sign-out) | `DashboardShell.tsx` | **Icon-only header controls share one primitive.** Every icon-only header/utility button renders through `SecondaryButton` (or `Button`), never a bespoke `<button>` with its own sizing. |
| The three dashboard summary cards (`DashboardPage.tsx` `SummaryCard`, lines 246-258) all use the identical neutral `bg-muted text-muted-foreground` icon treatment regardless of what they represent, so the page reads as flat and the existing `--success`/`--primary` tokens go unused here | Dashboard screenshot | **Card icon wells carry semantic weight.** A summary/stat card's icon well uses the token that matches its meaning (`bg-primary/10 text-primary` for the headline metric, `bg-success/10 text-success` when a status card's current value is the healthy/positive one, `bg-muted text-muted-foreground` only for purely neutral information) instead of one neutral treatment for every card on the page. |
| The same error-panel markup (`rounded-xl border border-destructive/40 bg-destructive/10 p-{5,6} shadow-soft`, `role="alert"`) is copy-pasted across at least `DashboardPage.tsx`, `UsersPage.tsx`, `TenantsPage.tsx`, `TenantHome.tsx`, `TenantScopePage.tsx`, `RolesPage.tsx`, `InvitationsPage.tsx`, `AuditLogPage.tsx` | `grep` across `src/web/src/pages` | **One shared state-panel primitive.** A page-level "X is unavailable / X is empty" panel (icon + title + description + optional retry action) is one shared component, not eight independent copies. Inline, one-line form-field errors (for example the `text-sm text-destructive` strings inside `RolesPage.tsx`/`InvitationsPage.tsx` forms) are a different, smaller pattern and are not folded into this component. |
| On mobile, the login page's hero section (`min-h-[42vh]`, `LoginPage.tsx` line 77) renders before the sign-in form in DOM/flow order (the `grid` has no column split below `lg:`), so a mobile visitor scrolls past the full hero before reaching the email field | Login mobile screenshot | **On an auth-style split screen, the form is reachable without scrolling on a mobile viewport.** Below the desktop breakpoint, the interactive form takes visual priority over the brand hero; the hero may still appear, but reordered/condensed so it does not push the form below the fold on a 390×844 viewport. |
| The dashboard's three cards leave roughly half the 1440×900 viewport empty below them, and the same happens on every other page in this list while its data is loading/erroring | Dashboard/roles screenshots | **Don't invent content to fill the page.** The fix for a sparse page is giving its real content more visual weight (semantic tinting, slightly firmer card padding/min-height, a subtle top accent), never adding a metric, card or section the API does not provide. `AGENTS.md`'s "no speculative backend" spirit applies to the frontend too: no fake data, no placeholder widgets. |

## Scope, split into three front tasks

**F025 — shell and brand foundations (do first; everything else builds on
the shared shell):**
- Fix `docs/design-system.md`: add the "one brand mark per screen," "icon
  buttons share one primitive," and "don't invent content" rules above to
  the existing document (as new bullets/subsections in the relevant
  existing sections — Components, Visual anti-patterns — not a rewrite).
- `ShellNav.tsx`: `BrandRow` stays the one sidebar/drawer brand mark; no
  code change required there unless the header duplication fix below
  reveals one.
- `DashboardShell.tsx`: remove the redundant literal "TenantForge" text
  from the header caption (line 154's `<p>`); keep the tenant-scope badge
  (`Building2` chip) that already conditionally appears there. Rebuild the
  theme toggle as a `SecondaryButton` instance instead of the bespoke
  `<button>` (lines 170-178), matching the menu/collapse/sign-out buttons'
  sizing exactly.
- `LoginPage.tsx`: remove one of the two duplicate "TenantForge" labels in
  the hero (lines 85 and 88 both render the literal word) — keep exactly
  one, paired with the shield mark.

**F026 — depends on F025 (shared `StatePanel` + dashboard density):**
- Add `src/web/src/components/ui/StatePanel.tsx`: one component covering
  the page-level error/empty pattern found in the eight files above —
  icon, title, description, optional action (retry button) slot, and a
  `tone` prop (`destructive` today; leave room for a future `muted`/empty
  tone without building it speculatively now).
- Replace the duplicated markup in `DashboardPage.tsx`, `UsersPage.tsx`,
  `TenantsPage.tsx`, `TenantHome.tsx`, `TenantScopePage.tsx`,
  `RolesPage.tsx`, `InvitationsPage.tsx`, `AuditLogPage.tsx` with
  `StatePanel`, preserving each page's exact current copy, icon and retry
  behavior — this is a visual/structural refactor, not a copy or behavior
  change.
- Apply the semantic-tinting rule to `DashboardPage.tsx`'s `SummaryCard`:
  محیط (environment) stays neutral (`bg-muted`), وضعیت API (`apiStatus`)
  uses `bg-success/10 text-success` when `summary.apiStatus === 'Healthy'`
  and the existing muted treatment otherwise (mirroring the dot indicator
  already in the same card), مدیران پلتفرم (`platformAdminCount`) uses
  `bg-primary/10 text-primary` as the headline platform metric.
- Give the three summary cards slightly firmer presence (a touch more
  padding and/or a `min-h` so they don't look like three small chips lost
  in the page) without adding any new card, metric or section.

**F027 — depends on F025 (login mobile priority; independent of F026):**
- `LoginPage.tsx`: on the mobile (below `lg:`) layout, the form section
  must be reachable without scrolling past the full hero — reorder with
  `order` utilities (hero `order-2 lg:order-1`, form `order-1 lg:order-2`)
  or an equivalent restructure that keeps the desktop split-screen layout
  pixel-identical at `lg:` and above.
- Visually de-emphasize the development-credentials box (currently the
  same `rounded-xl border ... shadow-soft` weight as the sign-in form
  itself) so it reads as a secondary developer aid, not a second form —
  for example, drop its shadow and use a quieter border/background than
  the primary card.

## Non-goals

- No new design token is introduced; every change above uses tokens
  already defined in `src/web/src/index.css` (`--success`, `--primary`,
  `--muted`, etc.).
- No new API contract, endpoint or invented metric.
- No change to `src/api/**` or `src/modules/**`.
- No frontend test is created, run or modified (frontend test ownership is
  unchanged per `AGENTS.md`).
- `tenantforge-ui-system` is not replaced or forked; these are additive
  rules inside the existing `docs/design-system.md`.

## Verification (all three tasks)

- `npm run build` and `npm run lint` succeed in `src/web/`.
- Real browser demo at 1440×900, 1024×768 and 390×844 for every page this
  slice touches; capture screenshots.
- No browser console error introduced.
- Visual diff check: every page's actual data, copy, icon choice and retry
  behavior is unchanged — only presentation (spacing, color, component
  boundary, DOM order) changed.
- `docs/design-system.md` reflects the four new rules (F025) by the time
  F027 is delivered; each task states in its own PR whether it touched the
  document.
