---
id: F025
slice: S25
title: Fix duplicated branding and inconsistent header controls in the shared shell
agent: ui-engineer
source: tasks/slices/025-visual-polish-pass.md
---

# Objective

Remove the redundant "TenantForge" wordmark repeated across the shell
header, sidebar and login hero, and make the header's theme-toggle button
use the same button primitive as its neighbors. Record the new rules this
establishes in `docs/design-system.md`. No behavior, route or data change
— visual/structural only.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` completely
first. Read the complete `tasks/slices/025-visual-polish-pass.md` — it is
the authoritative contract for this task and the two that follow it
(F026, F027); this Spec restates only the F025 portion.

This gap was found by running the real app in a browser and screenshotting
it. Re-verify every cited line against the current file before editing —
the file may have moved since this Spec was written.

# Scope

1. `src/web/src/components/shell/DashboardShell.tsx`:
   - In the header (around line 154), remove the literal `TenantForge`
     text node from the small caption `<p>`. Keep the conditional
     tenant-scope badge (`Building2` icon + tenant name/`مستأجر`) that
     already renders inside the same `<p>` — only the redundant wordmark
     text goes, not the badge.
   - Replace the bespoke theme-toggle `<button>` (around lines 170-178,
     the one with `className="inline-flex min-h-10 items-center gap-2
     rounded-md border border-border bg-surface px-3 ..."`) with a
     `SecondaryButton` instance, matching how the sidebar-collapse and
     sign-out buttons in the same header are built (same
     `aria-label`/icon-swap logic, now going through the shared
     component instead of duplicated classes).
2. `src/web/src/pages/LoginPage.tsx`: the hero panel currently renders the
   word "TenantForge" twice (once beside the shield icon around line 85,
   once as a standalone label around line 88). Keep exactly one — the one
   paired with the shield mark and tagline; remove the standalone
   duplicate label. Do not change the heading (`h1`) or body copy below it.
3. `src/web/src/components/shell/ShellNav.tsx`'s `BrandRow` is the
   correct, single sidebar/drawer brand mark — do not change it unless
   inspecting the header fix above reveals it must, and if so, explain
   exactly why in your plan before editing.
4. Update `docs/design-system.md`: add these two rules where they fit the
   existing document structure (likely the "Components" and "Visual
   anti-patterns" sections) — do not rewrite the document, add to it:
   - "One brand mark per screen": a screen shows the TenantForge mark
     (icon + wordmark) exactly once; a secondary header caption never
     repeats the wordmark, it carries scope/context information only.
   - "Icon-only header controls share one primitive": every icon-only
     header/utility button renders through the existing `SecondaryButton`
     (or `Button`) component, never a bespoke `<button>` with its own
     sizing classes.

# Acceptance

- Grep for the literal string `TenantForge` in `DashboardShell.tsx` and
  `LoginPage.tsx` after the change: it appears exactly once per rendered
  screen (header no longer renders it at all; the login hero renders it
  once).
- The theme-toggle button in `DashboardShell.tsx` is a `SecondaryButton`
  instance; its rendered height/border/radius are visually identical to
  the adjacent sign-out/collapse buttons (verify in the browser, side by
  side).
- The tenant-scope badge in the header still renders correctly both inside
  and outside tenant scope (unchanged behavior).
- `docs/design-system.md` contains both new rules, placed in a sensible
  existing section, without disturbing the rest of the document's content
  or tone.
- No visual regression to the sidebar `BrandRow`, the collapsed-sidebar
  icon rail, or the mobile drawer header.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual (real browser, not just static reading):

- Desktop (1440×900): open `/dashboard` (platform admin) and confirm the
  header no longer shows a redundant "TenantForge" caption; confirm the
  theme toggle now visually matches the other header buttons; toggle it
  and confirm it still switches themes correctly.
- Same check inside a tenant scope (`/t/:tenantId`) to confirm the
  tenant-name badge still renders where the wordmark text used to be.
- Mobile (390×844): open the drawer and confirm `ShellNav`'s `BrandRow` is
  unaffected.
- `/login` desktop and mobile: confirm exactly one "TenantForge" label
  remains in the hero, paired with the shield icon, and the rest of the
  hero copy is unchanged.
- No new browser console error.

# Lifecycle

Add row `F025` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F024`, and Spec link
`tasks/front/F025-shell-and-brand-foundations.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/025-visual-polish-pass.md` is the permanent record
and is never deleted.
