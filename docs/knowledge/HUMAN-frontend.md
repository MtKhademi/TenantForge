# Frontend knowledge for people

A plain-language description of what the TenantForge web app does today, how
the important pieces work and why they were built that way.

This file is **not** required reading for an implementing agent — that is
[AGENT-frontend.md](AGENT-frontend.md). Read this one when you want to
understand the interface rather than change it.

## What the frontend is

A React 19 + TypeScript single-page app built with Vite, styled with Tailwind
CSS v4 tokens and a small set of local shadcn-style primitives. The interface
is Persian and right-to-left by default, with a right sidebar on desktop and a
navigation drawer on mobile.

It talks to the .NET API over `/api`. In development the Vite dev server
proxies that path to the API host, so the browser stays same-origin and no CORS
configuration is needed.

## How a screen is put together

- **`src/web/src/App.tsx`** declares every route. Platform routes sit at the root,
  tenant-scoped routes under `/t/:tenantId/...`, and the public storefront
  under `/shop/:tenantId/...`.
- **`src/web/src/pages/`** holds one component per screen. A page composes layout and
  states; it does not call the network itself.
- **`src/web/src/features/<area>/`** holds the data access for one capability: a
  `…Types.ts` file with the wire types and typed error classes, a `…Adapter.ts`
  file that performs the requests, and a React context when the capability has
  shared state (session, tenant scope, theme).
- **`src/web/src/components/ui/`** and **`src/web/src/components/shell/`** hold the shared
  primitives and the application shell.

A page therefore reads: render states, call an adapter method, map typed errors
to visible messages. Swapping mock data for a real API call means replacing the
adapter, not rewriting the screen.

## What is delivered today

- login, development sign-in helper and session recovery;
- a platform dashboard, user list and user creation;
- tenant creation, a tenant list and a tenant switcher;
- tenant member visibility with server-enforced isolation;
- roles and a permission matrix rendered from the server catalog;
- permission-aware navigation for invitations and audit logs;
- invitation creation and a pending-invitation list with expiry times;
- a tenant audit log;
- Shop administration: categories, products, shipping rates and coupons;
- a Shop storefront: category browsing, product detail, cart, checkout, order
  review, a sandbox bank page, a payment result page and guest order tracking.

Live status for everything else is in `tasks/TASKS.md`.

## Decisions worth knowing, and why

**The backend contract is the source of truth.** The frontend never defines its
own permanent shape for server data. When a screen has to ship before its API
exists, the mock is written against the agreed backend contract — same field
names, nullability and error codes — so connecting it later replaces one
adapter and touches no component.

**Pages never call `fetch`.** All network access goes through a feature
adapter. That is what makes mock-first delivery possible and keeps error
handling, timeouts and token attachment in one place per capability.

**Typed errors, not error strings.** Adapters throw
`ApiUnavailableError`, `SessionExpiredError`, validation, forbidden and
conflict error classes. Pages switch on the type, so a wording change never
breaks behavior.

**Every screen ships its states.** Idle, loading, empty, error, success and the
relevant 401/403 are part of the work, not polish. A screen that only renders
the happy path is not considered done.

**Hiding UI is not security.** Permission-aware navigation exists for clarity;
the server still rejects the request. The 403 state is implemented and
reachable.

**RTL first, LTR compatible.** Components use logical direction properties, so
the same primitives work if a left-to-right locale is added later.

**Design tokens over local styling.** Colors, spacing, radii and shadows come
from `index.css` and the shared primitives. Per-page design systems and
default-template visuals are explicitly avoided.

## Running it

```bash
cd src/web
npm install
npm run dev
```

Then open `http://localhost:5173/login`. Start PostgreSQL and the API first, or
every screen will show its "API unavailable" state.

In `Development` the backend seeds `admin@tenantforge.local` /
`local-development-password`. The login page shows this helper only when
`import.meta.env.DEV` is true; production builds never display credentials.

Checks:

```bash
cd src/web
npm run lint
npm run build
```

Vitest and Playwright are installed, but frontend test ownership is decided by
task policy — the UI role does not run them by default.

## Current limitations

- Invitation acceptance, registration from an invitation, and resend/revoke are
  not implemented; the invitation screen shows pending records only.
- There is no long-lived session management or refresh-token handling.
- Payment screens currently exercise the sandbox gateway.
- Parts of the Shop experience are still delivered against mocks; the ledger
  records which capability is mock-backed and which is connected.

## Where to look next

- `docs/design-system.md` — visual language, required states, anti-patterns
- `docs/design/shop/frontend-contract-boundary.md` — the Shop client seam
- `docs/design/shop/http-contracts.md` — Shop wire shapes
- `src/web/README.md` — running the app locally
- `docs/user-guide/README.md` — the Persian end-user guide

## Maintaining this file

Read it before editing so existing explanations are preserved. Add or revise
only the sections a delivered task actually changed, and describe the verified
implementation rather than the plan. Do not paste large code blocks, secrets or
task-specific debugging detail.
