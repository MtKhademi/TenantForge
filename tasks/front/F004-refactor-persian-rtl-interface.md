---
id: F004
slice: S02
title: Refactor the application into a Persian RTL interface
agent: ui-engineer
source: tasks/slices/002-authenticated-shell.md
---

# Objective

Make Persian and right-to-left behavior the default application contract before
new product screens are built, so future pages are implemented once for Iranian
administrators instead of being translated and mirrored later.

## Context

The current authenticated shell, login and dashboard use English copy and an LTR
layout. TenantForge is intended for Iranian teams; mixed English/Persian screens,
a left-side navigation rail and late RTL conversion would create avoidable
rework and a confusing product experience.

## General implementation

- Set the document language and direction to Persian RTL at the application
  boundary, not separately on individual pages.
- Translate every user-visible string in the currently implemented login,
  authentication, shell and dashboard flows, including loading, validation,
  empty, error, retry, session-expired, navigation, tooltip and accessibility
  text.
- Keep the TenantForge brand name and internal API, route, permission and code
  identifiers unchanged where they are not user-facing copy.
- Apply a readable Persian font stack and typography through the existing design
  tokens and shared UI primitives.
- Replace direction-specific layout rules with logical properties so spacing,
  borders, alignment and future components inherit RTL correctly.
- Place primary navigation on the right and mirror direction-sensitive controls
  and icons where their meaning depends on direction.
- Keep this release Persian-first; do not add a language switcher or a
  speculative multi-language framework.

## Acceptance criteria

- [ ] The root document renders with `lang="fa"` and `dir="rtl"`.
- [ ] Login, protected shell and current dashboard contain no accidental
      English product copy or mixed-direction labels.
- [ ] Navigation appears on the right and reading order, alignment, forms,
      validation messages and keyboard focus order remain coherent.
- [ ] Shared components use logical RTL-safe layout rules instead of page-level
      mirroring hacks.
- [ ] Brand names and technical identifiers remain stable while user-facing
      roles and concepts have clear Persian labels.
- [ ] Desktop and mobile layouts have no horizontal overflow, clipped content or
      browser-console errors.
- [ ] Existing authentication, routing and theme behavior remains unchanged.

## Verification

- [ ] Run frontend format, lint, type-check, unit tests and production build.
- [ ] Add or update tests for document locale, Persian copy and RTL placement.
- [ ] Demonstrate login, dashboard, loading/error and protected-route behavior
      in a real browser at desktop and mobile widths.
- [ ] Capture browser evidence showing the Persian RTL shell and a clean console.
