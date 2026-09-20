---
name: tenantforge-ui-system
description: Build or review TenantForge's React interface with its consistent B2B SaaS visual language, responsive layout, accessibility states, RTL/LTR compatibility and browser-based visual verification. Use for every TenantForge page, component, frontend scaffold, UI review or visual QA task.
---

# TenantForge UI system

Read `docs/design-system.md` before making visual decisions.

## Workflow

1. Read the active task and list only its required pages, interactions and states.
2. Inspect existing tokens and reusable components before creating new ones.
3. Define missing foundations once; do not introduce page-local design systems.
4. Build semantic structure and keyboard behavior before decorative polish.
5. Implement the relevant idle, loading, error, empty, success, `401` and `403` states named by the task.
6. Verify desktop, tablet and mobile behavior in a real browser.
7. Inspect focus, overflow, contrast, console errors and unexpected layout shifts.
8. Capture evidence and fix visible defects before declaring completion.

## Constraints

- Use shadcn/ui as customizable primitives, not as an unchanged template.
- Reuse project tokens; do not scatter arbitrary color, spacing, radius or shadow values.
- Preserve logical direction so reusable UI works in LTR and RTL.
- Use one consistent icon library; do not use emoji as interface icons.
- Prefer clear hierarchy and restrained surfaces over nested cards and decorative effects.
- Do not create dashboards, charts or menu items that are unrelated to the active task.
- Do not treat hidden frontend controls as authorization.

## Mock-first contract parity

When a task belongs to an explicitly mock-first feature batch:

1. Read the paired backend Spec and its persistent HTTP-contract document
   before defining mock data. Treat their routes, request/response members,
   nullability, enum strings, pagination and problem reason codes as fixed
   input. After backend delivery deletes its executable Spec, verify the
   persistent contract against delivered C# records and integration tests.
2. Define the wire shape once in a capability contract file with TypeScript
   types and runtime Zod schemas. Do not use `any`, unchecked casts or a second
   UI-only copy of the same server DTO.
3. Put behavior behind a capability client interface. The UI consumes that
   interface through the project composition seam; it never imports fixtures
   and never calls `fetch` directly.
4. Make the mock client satisfy the same interface and schemas reserved for the
   later HTTP client. Mock stable IDs, UTC timestamps, validation, permission,
   conflict, expiry and rate-limit states named by the contract.
5. Keep mock scenario controls development-only and report `Data source: mock`
   in review evidence.
6. In the later connection task, preserve accepted components and replace
   exactly one mock client binding with its HTTP implementation. Parse external
   responses through the existing schemas. Never silently fall back to mock
   data after a real request fails.
7. If delivered backend records differ from the accepted contract, stop and
   present the exact mismatch before editing UI and backend shapes together.

## Completion report

Report:

- implemented states;
- viewport sizes verified;
- screenshot or browser evidence paths;
- accessibility and console checks;
- mocked versus real data;
- intentionally deferred UI work.
