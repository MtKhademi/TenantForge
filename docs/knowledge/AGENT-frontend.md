# Frontend agent knowledge

Operational memory for the agent running `/front-task`. Read this file
completely before implementing any frontend task. It describes the current
codebase, not its history. Keep it short — link to the detailed document
instead of copying it here.

Human-facing explanations live in [HUMAN-frontend.md](HUMAN-frontend.md). Do
**not** read that to implement a task.

## Fast facts

| Fact | Value |
| --- | --- |
| App root | `src/web` — every command runs from there |
| Stack | React 19, TypeScript, Vite, Tailwind CSS v4 |
| Routing | `react-router-dom` v7, nested routes in `src/web/src/App.tsx` |
| Forms & validation | `react-hook-form` + `zod` v4 (`@hookform/resolvers`) |
| UI primitives | `@base-ui/react` + local shadcn-style components, `lucide-react` icons |
| Import alias | `@/` → `src/web/src` |
| Direction | Persian RTL (`<html lang="fa" dir="rtl">`), must stay LTR-compatible |
| Lint | `oxlint` |

## Layout

```text
src/web/src/
  App.tsx                 # every route
  main.tsx, index.css     # bootstrap + Tailwind tokens
  components/ui/          # Button, TextInput, StatePanel, PaginationControls, Tooltip
  components/shell/       # DashboardShell, ShellNav, TenantSwitcher, SessionLoadingScreen
  components/shop/        # shared Shop UI such as ProductGalleryEditor/ProductMediaImage
  features/<area>/        # data access + types + context for one capability
  pages/                  # platform and tenant pages
  pages/shop/admin/       # CategoriesPage, ProductsPage, ShippingRatesPage, CouponsPage
   pages/shop/storefront/  # StorefrontLayout, StorefrontCatalogPage, CategoryPage,
                           # ProductDetailPage, CartPage, CheckoutPage, OrderReviewPage,
                           # SandboxBankPage, PaymentResultPage, OrderTrackingPage
  lib/utils.ts            # cn() class merge
  test/                   # vitest setup — off limits, see Ownership
```

## Ownership

- Own `src/web/**` and browser evidence.
- **Do not inspect, create, update or run frontend tests.** That means
  `src/web/src/**/*.test.tsx`, `src/web/src/test/**`, `src/web/e2e/**`,
  `npm test` and `npm run test:e2e`. Frontend test ownership is decided by task
  policy, and the default policy excludes it.
- Never edit `src/api/**`, `src/modules/**`, backend tests, migrations or
  authorization policies. Report a contract problem instead of changing it.
- Build only the active screen and the smallest primitives it needs. No screens
  beyond the active task.

## Feature pattern (this is the one to copy)

Each capability owns a folder under `features/`:

- `<name>Types.ts` — TypeScript types plus typed error classes
  (`ShopValidationError`, `ShopForbiddenError`, `UserConflictError`, …).
- `<name>Adapter.ts` — the data access seam. The page imports the adapter, never
  `fetch` directly.
- `<Name>Context.tsx` — React context when the capability has shared state
  (`AuthContext`, `TenantScopeContext`, `ThemeContext`).

An HTTP adapter follows `src/web/src/features/shop/shopCatalogAdapter.ts`:

- `const REQUEST_TIMEOUT_MS = 8_000` and an `AbortController` per request;
- `authHeaders(accessToken)` → `{ Authorization: 'Bearer ...' }`;
- a network failure throws `ApiUnavailableError`, a 401 throws
  `SessionExpiredError` (both from `@/features/auth/authTypes`);
- an RFC 7807 body's `errors` map is translated into field errors;
- user-visible messages are Persian.

Read the nearest existing adapter before writing a new one.

## Contract rules

- The **backend contract is the source of truth**. Never invent a different
  permanent frontend data model.
- Mock data is allowed only when the active task says so, and its shape must
  match the backend contract exactly — same member names, nullability, enum
  strings, pagination and problem reason codes.
- `PaginationMetadata` has six members: `pageNumber`, `pageSize`, `totalCount`,
  `totalPages`, `hasPreviousPage`, `hasNextPage`.
- Read the paired backend Spec and the persistent contract document
  (`docs/design/shop/http-contracts.md`, `docs/contracts/iam.md`) before
  defining any mock.
- A later connect task replaces exactly one mock binding with its HTTP
  implementation. Never silently fall back to mock data after a real request
  fails.
- Gate development-only scenario controls behind `import.meta.env.DEV`.

### Shop client seam

`src/web/src/features/shop/contracts/` holds Zod wire contracts and
`src/web/src/features/shop/clients/` holds mock/HTTP-ready client ports. The app
mounts one `ShopClientsProvider` around all routes; each bound slot is replaced
individually by its connect task (F054 → `media`, F055 → `discovery`,
F056 → `categories`). Bound so far: `media` → `mockShopMediaClient` (F044),
`discovery` → `mockShopDiscoveryClient` (F045) and `categories` →
`mockShopCategoryClient` (F046). Mock Shop scenario controls are development-only
and selected through the provider, never by importing fixtures into pages. Each
capability's dev switcher is a separate `Dev…ScenarioSwitcher.tsx` that the
provider's dev-only `DevScenarioToolbar` loads independently; new capabilities
add their own switcher and position it so it does not overlap another switcher's
fixed corner (media: `bottom-4 end-4`, discovery: `bottom-4 start-4`,
categories: `bottom-[4.75rem] start-4`). See
`docs/design/shop/frontend-contract-boundary.md`.

`CategoryPage` (storefront) is deliberately mixed until F055/F056: the grouped
nav bar in `StorefrontLayout`, the max-two-level breadcrumb and the admin
`CategoriesPage` all consume the `categories` client slot (mock now), while the
flat category grid and the product grid on `CategoryPage` still call the real
`storefrontAdapter` — those calls 404 in the mock-first phase and are the only
expected console noise on anonymous storefront routes.

## Authentication and permissions

- The session lives in `sessionStorage` under `tenantforge:auth:session` and is
  recovered on load by `AuthContext`.
- Tenant scope comes from `TenantScopeContext`; tenant routes are
  `/t/:tenantId/...`, the public storefront is `/shop/:tenantId/...`.
- Permission keys are read from the **server** catalog. The local mirror is
  `src/web/src/features/roles/permissionCatalog.ts` and
  `src/web/src/features/roles/roleTypes.ts`
  (`SHOP_CATALOG_MANAGE_KEY = 'Shop.Catalog.Manage'`,
  `SHOP_SHIPPING_MANAGE_KEY = 'Shop.Shipping.Manage'`). Adding a key means
  updating both files, and the backend must already expose it.
- **Hiding a control is presentation, never authorization.** Still render the
  403 state the task names.

## Required UI states

For every screen the task names: idle, loading, empty, error, success, and the
relevant `401` / `403`. Also required on every task:

- responsive desktop and mobile behavior — verify 1440×900, 1024×768, 390×844;
- semantic HTML and accessible names;
- visible focus;
- logical direction so components work in RTL and LTR;
- no new browser console error.

Reuse tokens from `index.css` and existing `components/ui` primitives. Do not
scatter arbitrary colors, spacing, radii or shadows, and do not create a
page-local design system.

## Commands

```bash
cd src/web
npm install
npm run dev        # http://localhost:5173
npm run build      # tsc -b && vite build
npm run lint       # oxlint
npm run preview
```

`npm test` and `npm run test:e2e` exist but are **not** run by this role.

The dev server proxies `/api` to the API host. Inside WSL it resolves the
Windows gateway IP automatically; override with `VITE_API_PROXY_TARGET`.

## Known traps

1. Every npm command needs `cd src/web` first. A bare `npm run build` from the
   repository root fails.
2. `npm run build` runs `tsc -b`, so a type error fails the build even when
   Vite alone would succeed.
3. The API must be running (and PostgreSQL up) before browser verification, or
   every adapter surfaces `ApiUnavailableError`.
4. In WSL, the Windows-hosted API is not reachable on `127.0.0.1` — the Vite
   proxy handles this, direct `fetch` to `localhost:5000` does not.
5. The app is RTL by default. Use logical properties (`ms-`/`me-`,
   `start`/`end`), not `left`/`right`.
6. `/shop/:tenantId` renders `StorefrontCatalogPage` as its `index` route (since
   F045); `categories/:categorySlug` still renders `CategoryPage`. Check
   `src/web/src/App.tsx` before claiming a route is free.
7. Backend `ShopPaymentAttempt` redirect URLs are not all absolute. Treat any
   gateway redirect as untrusted input and validate scheme and host before
   navigating.
8. On WSL/NTFS the Vite dev server can serve stale code after edits (HMR misses
   the change): if a page does not reflect a change you know is on disk, restart
   the dev server before debugging the code.
9. In the mock-first Shop batch, the dev-only scenario toolbars are `position:
   fixed` at the viewport bottom. On short pages they hit-test over content, so
   Playwright pointer clicks (even `force: true`) are intercepted; browser
   evidence scripts dispatch a DOM `el.click()` inside `evaluate()` instead.
   Production builds never contain the toolbars (`import.meta.env.DEV`).
10. The Vite dev server resolves its `/api` proxy target (WSL gateway IP) ONCE
   at startup. When that gateway IP changes, `/api/*` through the port returns
   connection failure while `curl` to `:5000` still works — the app then looks
   like a logout loop (auth bootstrap 404s → redirect to `/login`). Restart the
   dev server with an explicit target:
   `VITE_API_PROXY_TARGET=http://127.0.0.1:5000 npm run dev -- --host 127.0.0.1
   --port 5173 --strictPort` (the API binds 127.0.0.1 in this setup).
11. In Playwright, assigning `select.value` inside `evaluate()` does NOT fire
   React's `onChange`, so a react-hook-form field stays stale (e.g. a mock
   scenario keyed on a non-null `parentCategoryId` never triggers). Use
   `page.selectOption()` (fires the proper events) for controlled `<select>`s.

## Decisions future tasks must preserve

- Pages consume a typed adapter/client interface, never `fetch` directly and
  never a fixture import.
- One wire shape per capability, derived from the backend contract, with no
  `any` and no unchecked cast.
- Persian RTL first, LTR-compatible.
- Design tokens and shared primitives over per-page styling.
- Frontend tests remain outside this role's ownership.

## More detail

- `AGENTS.md` — shared rules, ownership, ledger lifecycle, Git safety
- `docs/design-system.md` — visual language, states, anti-patterns
- `docs/design/shop/http-contracts.md` — Shop wire shapes
- `docs/design/shop/frontend-contract-boundary.md` — the client seam
- `.opencode/skills/tenantforge-ui-system/SKILL.md` — UI workflow and
  mock-first contract parity
- `src/web/README.md` — running the app locally

## Maintaining this file

Update it in the same task that changes a fact above. Add durable facts only:
no debugging notes, no speculation, no abandoned approaches, no secrets, no
large code blocks. If a task changed nothing here, say so in the completion
report instead of padding the file.
