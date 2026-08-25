# S00 — UI foundation

## Executable tasks

- Front: `F001`
- Backend: None

## Owner

`ui-engineer`

## Depends on

- None.

## Visible outcome

A contributor can start a React application and see a polished TenantForge login page. Submitting the documented mock administrator opens a responsive dashboard shell with sidebar, header, theme control and placeholder welcome content.

## Demo

1. Install dependencies and start the web application.
2. Open `/login` on desktop.
3. Submit the task-defined mock administrator.
4. See `/dashboard` with TenantForge navigation and welcome content.
5. Refresh and verify the mock session behavior documented by the implementation.
6. Repeat the main flow at a mobile viewport.
7. Submit invalid credentials and see a clear inline error.

## UI states

- Empty login form.
- Validation errors.
- Submitting state.
- Invalid mock credentials.
- Successful navigation.
- Desktop sidebar.
- Mobile navigation drawer.
- Light and dark theme.

## API contract

None. Authentication is an explicit frontend mock isolated behind an interface that can be replaced in S01.

## Backend learning goal

None.

## Scope

- Create `src/web` with React, TypeScript and Vite.
- Introduce Tailwind CSS and shadcn/ui through their current supported setup.
- Add routing for `/login` and `/dashboard`.
- Create the minimum design tokens and application-shell components.
- Implement a clearly named development mock authentication adapter.
- Add frontend tests for validation and navigation.
- Add Playwright smoke coverage or equivalent browser automation for the demo flow.

## Out of scope

- .NET projects or API endpoints.
- Real tokens, cookies or persistent authentication.
- Register, forgot-password or social-login screens.
- Dashboard metrics, charts or tables.
- Tenant switcher, users, roles and permissions.
- Marketing website.

## Acceptance criteria

- [ ] `src/web` starts with one documented command.
- [ ] Login and dashboard shell match `docs/design-system.md` without looking like an unchanged template.
- [ ] Invalid input and invalid mock credentials are distinguishable.
- [ ] Submit is protected from duplicate clicks and shows progress.
- [ ] Desktop, tablet and mobile layouts are verified in a real browser.
- [ ] Keyboard navigation and visible focus work through the login form and shell navigation.
- [ ] Light and dark themes remain readable.
- [ ] Mock authentication code is isolated and explicitly temporary.
- [ ] Frontend tests pass and no console error appears.
- [ ] Desktop and mobile screenshots are included in the review evidence.
- [ ] No backend or future page is created.
