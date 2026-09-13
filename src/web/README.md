# TenantForge web

The web app is the React/Vite frontend for TenantForge. It is a Persian RTL administration interface with a right sidebar on desktop and a mobile navigation drawer.

## Run locally

Start the API first from the repository root, then run the web app:

```bash
npm install
npm run dev
```

Open:

```text
http://localhost:5173/login
```

The Vite dev server proxies `/api` to the API host configured in `vite.config.ts`. In WSL it resolves the Windows host gateway automatically unless `VITE_API_PROXY_TARGET` is set.

## Development sign-in

In `Development`, the backend seeds a platform administrator:

```text
Email: admin@tenantforge.local
Password: local-development-password
```

The login page displays this helper only when `import.meta.env.DEV` is true. Production builds do not show sample credentials.

## Scripts

```bash
npm run lint
npm run build
npm run dev
npm run preview
```

Frontend tests are installed but are not part of every UI task. Follow the active task policy in `AGENTS.md` and `tasks/TASKS.md`; the ui-engineer role does not inspect, edit or run frontend tests unless a task explicitly changes that policy.

## Current UI scope

The frontend currently covers:

- login and session recovery;
- platform dashboard, users and tenants;
- tenant member list and tenant switching;
- roles and server-provided permission catalog;
- permission-aware invitations and audit navigation;
- pending invitation creation with built-in or tenant custom role names.

Invitation acceptance, registration from invite, email delivery, resend/revoke and server-side pagination UI remain future slices.
