---
id: F005
slice: S02
title: Refactor the collapsible RTL application sidebar
agent: ui-engineer
source: tasks/slices/002-authenticated-shell.md
---

# Objective

Repair the authenticated shell so expanding and collapsing the right-side
navigation produces a stable, understandable and accessible layout without
pushing, clipping or hiding the main content.

## Context

In the current shell, collapsing the sidebar leaves the content positioned from
the previous expanded width, pushes the page outside the viewport and reduces
navigation items to meaningless dots. The final behavior must be designed and
verified in the Persian RTL layout introduced by F004.

## General implementation

- Refactor the shell layout with the existing design tokens and shared
  components so the main region automatically reflows between expanded and
  collapsed sidebar widths.
- Use RTL-safe logical sizing, positioning, borders and transitions; do not fix
  the screenshot by globally hiding overflow or adding viewport-specific
  offsets.
- In collapsed mode show the real navigation icon for every item, retain a clear
  active state and provide a Persian tooltip and accessible name.
- Make the collapse/expand control visually and semantically indicate the next
  action, including the correct direction in RTL.
- Keep the brand mark recognizable in compact mode and hide only text that
  cannot fit.
- Preserve navigation, authentication, theme and route behavior.
- Use an appropriate mobile navigation pattern instead of forcing the desktop
  icon rail into narrow viewports.

## Acceptance criteria

- [ ] Expanding or collapsing the right-side sidebar reflows the header and main
      content into the available width without clipping, overlap or horizontal
      scrolling.
- [ ] Collapsed navigation uses meaningful icons rather than dots, and every
      item remains identifiable through Persian tooltips and accessible names.
- [ ] The active route is clearly visible in both expanded and collapsed modes.
- [ ] The toggle icon, placement, focus state and accessible label match its
      action and the RTL direction.
- [ ] Repeated toggling and route navigation do not leave stale widths,
      transforms or misplaced content.
- [ ] Desktop, tablet and mobile behavior is coherent with keyboard and pointer
      input.
- [ ] No global overflow rule masks a broken layout.
- [ ] No authentication, routing or theme regression is introduced.

## Verification

- [ ] Add focused component tests for expanded/collapsed state, active
      navigation and accessible labels.
- [ ] Add or update browser tests that toggle the sidebar and navigate between
      every currently available item.
- [ ] Verify at representative desktop, tablet and mobile widths with no
      horizontal overflow.
- [ ] Capture before/after browser evidence for expanded and collapsed states,
      including a clean browser console.
- [ ] Run frontend format, lint, type-check, unit tests, E2E tests and production
      build.
