# TenantForge UI direction

## Product character

TenantForge should look like a calm, capable B2B SaaS product—not a generic admin template and not a decorative AI landing page.

The visual personality is precise, quiet and trustworthy. Dense administration screens remain readable through strong hierarchy and spacing rather than excessive cards, gradients or shadows.

## Foundations

Use design tokens instead of unrelated literal values. The UI foundation task must define tokens for:

- canvas, surface and elevated surface;
- primary and muted text;
- borders and focus rings;
- brand, success, warning and destructive states;
- typography scale and line height;
- spacing, radius and shadow levels;
- sidebar dimensions and content maximum width;
- motion duration and easing.

Start with a neutral slate foundation and one restrained indigo/blue brand accent. Verify contrast in both light and dark themes before expanding the palette.

## Layout

- Desktop uses a collapsible left sidebar and a compact top header.
- Mobile uses a drawer navigation and preserves the primary page action.
- Page titles, descriptions and actions share a repeatable header pattern.
- Tables and forms should not be wrapped in unnecessary nested cards.
- Empty space should clarify grouping, not inflate page height.

## Components

Use shadcn/ui as accessible source-code primitives, then customize them into a coherent TenantForge system. Avoid leaving components in their default demo appearance.

All interactive components require:

- keyboard behavior;
- visible focus;
- disabled and busy states;
- accessible names;
- error and success feedback when relevant;
- reduced-motion support for non-essential animation.

## Required states

Every data-driven screen considers:

- initial loading;
- refetching without destructive layout shifts;
- empty result;
- validation failure;
- server failure;
- unauthenticated `401`;
- forbidden `403`;
- success feedback.

Only states relevant to the current slice must be implemented; future pages are not prebuilt.

## Responsiveness and direction

Validate at minimum:

- 1440 × 900 desktop;
- 1024 × 768 tablet;
- 390 × 844 mobile.

Layout and components must remain compatible with both LTR and RTL direction. English is the first interface language, but physical left/right assumptions should be avoided in reusable primitives.

## Visual anti-patterns

Avoid:

- excessive gradients and glow effects;
- glassmorphism across ordinary admin surfaces;
- large rounded cards for every content group;
- decorative charts with no product meaning;
- emoji used as the icon system;
- random spacing values;
- animation that delays routine administration;
- copied dashboard-template sections unrelated to the active task.

## Visual verification

Before finishing a UI task:

1. run the application in a real browser;
2. capture desktop and mobile screenshots;
3. inspect focus, overflow, loading and error behavior;
4. check browser console errors;
5. compare the result to the task's visible outcome;
6. fix obvious alignment, hierarchy and responsiveness defects before reporting completion.
