---
id: F027
slice: S25
title: Put the sign-in form before the hero on mobile and de-emphasize the dev-credentials box
agent: ui-engineer
source: tasks/slices/025-visual-polish-pass.md
---

# Objective

On the login page's mobile layout, make the sign-in form reachable without
scrolling past the full-height brand hero, and visually de-emphasize the
development-credentials helper box so it no longer competes with the
sign-in form for attention. No behavior, copy or desktop-layout change.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/025-visual-polish-pass.md` — it is the authoritative
contract for this task. This task depends on F025 (which removes the
duplicate "TenantForge" label in this same hero) being `done` first, so
you are editing the hero's already-corrected single-brand-mark state, not
the original duplicated one.

# Scope

`src/web/src/pages/LoginPage.tsx`:

1. The page root is a `grid` with `lg:grid-cols-[minmax(0,1fr)_minmax(31rem,0.78fr)]`
   and two direct `<section>` children: the hero (currently first in DOM,
   `min-h-[42vh]` below `lg:`) and the form (currently second). Below the
   `lg:` breakpoint, reorder so the form section is visually first and the
   hero second — for example `order-2 lg:order-1` on the hero section and
   `order-1 lg:order-2` on the form section. The desktop (`lg:` and above)
   visual result must be pixel-identical to today: same column split, same
   DOM content, only the `order` utility differs from its default.
2. Re-check the hero's mobile height/padding once reordered — if
   `min-h-[42vh]` still leaves too little room for its own heading and
   pushes it awkwardly, adjust the mobile-only sizing (not the `lg:`
   values) so the hero reads as a clean, complete panel at 390×844,
   whatever its position in the stack.
3. The development-credentials box (the block with the seeded admin email
   and password, rendered below the sign-in `form`) currently uses the
   same `rounded-xl border ... shadow-soft` weight as the sign-in `form`
   itself, so the two look like two competing forms. Reduce its visual
   weight: drop the shadow and/or use a quieter border/background (for
   example the existing `bg-muted`/`border-border` combination without
   `shadow-soft`) so it clearly reads as a secondary developer aid below
   the real form, not a second card of equal importance. Keep its exact
   copy and the environment-gating behavior that already hides it outside
   Development.

# Non-goals

- No change to the desktop (`lg:` and above) visual layout, column split
  or copy.
- No change to form validation, submission behavior, or the
  `sessionNotice`/credential-error panels' own logic (only their
  surrounding visual weight, if the slice's shared `StatePanel` from F026
  is already `done` and applies here — check the ledger; if F026 is not
  yet delivered, leave those panels exactly as they are and do not
  duplicate that work here).
- No new design token; reuse existing `border`/`muted`/`shadow-soft`
  tokens.

# Acceptance

- At 390×844, the sign-in form (email field, password field, submit
  button) is visible without scrolling on initial page load.
- At 1440×900 and 1024×768, the layout is visually identical to before
  this task (same column proportions, same content order left-to-right).
- The development-credentials box is visually subordinate to the sign-in
  form (lighter border/no shadow) while remaining fully legible and
  keeping its exact current copy and Development-only visibility.
- Keyboard navigation/tab order through the form still makes sense at
  every viewport (reordering visual position with CSS `order` must not
  produce a confusing tab order — verify this explicitly, since `order`
  changes visual position but not DOM/tab order by default; if tab order
  now feels wrong on mobile, note it and propose the smallest correction
  rather than silently shipping a confusing tab sequence).

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser:

- 390×844: load `/login` fresh (cleared session storage) and confirm the
  form is on screen without scrolling; tab from the top of the page and
  confirm the tab order still reaches the email field in a sensible
  number of tab presses, not after tabbing through the entire hero.
- 1024×768 and 1440×900: confirm the desktop layout is unchanged from
  before this task (compare against a screenshot taken before editing).
- Toggle the theme and confirm the de-emphasized dev-credentials box still
  has sufficient contrast in both themes.
- No new browser console error.

# Lifecycle

Add row `F027` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F027-login-mobile-priority.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/025-visual-polish-pass.md` is the permanent record
and is never deleted.
