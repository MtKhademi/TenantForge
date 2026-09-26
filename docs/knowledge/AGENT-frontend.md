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
   pages/shop/admin/       # CategoriesPage, ProductsPage, ShippingRatesPage, CouponsPage,
                             # ShopProfilePage (F047), OrdersPage + OrderDetailPage +
                             # OrderStatusBadge (F050)
    pages/shop/storefront/  # StorefrontLayout (header cart badge reads the
                            # cartLease slot, F048), StorefrontCatalogPage, CategoryPage,
                            # ProductDetailPage, CartPage, CheckoutPage, OrderReviewPage,
                            # SandboxBankPage, PaymentResultPage, OrderTrackingPage,
                            # PolicyPage (F047 — one reusable policy/about page)
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
F056 → `categories`, F057 → `profile`, F058 → `cartLease`, F059 → `coupons`,
F060 → `orders`, F061 → `orderOperations`, F062 → `payments`).
Bound so far: `media` → `mockShopMediaClient` (F044), `discovery` →
`mockShopDiscoveryClient` (F045), `categories` → `mockShopCategoryClient`
(F046), `profile` → `mockShopProfileClient` (F047), `cartLease` →
`mockShopCartLeaseClient` (F048), `coupons` → `mockShopCouponClient` (F049),
`orders` → `mockShopOrdersClient` (F050), `orderOperations` →
`mockShopOrderOperationsClient` (F051) and `payments` →
`mockShopPaymentsClient` (F052). Mock Shop scenario controls are
development-only and selected through the provider, never by importing fixtures
into pages. Each capability's dev switcher is a separate `Dev…ScenarioSwitcher.tsx`
that the provider's dev-only `DevScenarioToolbar` loads independently; new
capabilities add their own switcher and position it so it does not overlap another
switcher's fixed corner (media: `bottom-4 end-4`, discovery: `bottom-4 start-4`,
categories: `bottom-[4.75rem] start-4`, profile: `bottom-[8.5rem] start-4`,
cartLease: `bottom-[12.25rem] start-4`, coupons: `bottom-[16rem] start-4`,
orders: `bottom-[19.75rem] start-4`, orderOperations: `bottom-[23.5rem] start-4`,
payments: `bottom-[27.25rem] start-4`, rateLimit: `bottom-[31rem] start-4`).
See `docs/design/shop/frontend-contract-boundary.md`.

The `orders` slot (B042/F050) feeds the permission-gated admin `OrdersPage`
(list + URL filters `q`/`status`/`fromUtc`/`toUtc` + pagination, desktop table /
mobile cards) and read-only `OrderDetailPage` (snapshot items, customer/address,
totals, payment attempts capped at the **20 newest**). Both branch on
`Shop.Orders.View` via `useTenantPermissions`; a user without the key sees the
denied panel and the nav item is inert. Filters are **server-shaped**: the URL is
the source of truth, search is debounced 300 ms, and every filter change is a
history **push** (not `replace`) so back/forward traverses filter states. Mock
IDs are canonical 13-char TSIDs; `mockShopOrdersClient` is seeded only for the
demo tenant (see trap 15) so a foreign tenant scope 404s like a missing id.

The `orderOperations` slot (B043/F051) owns the fulfil/cancel actions on
`OrderDetailPage` through `OrderOperationsPanel` and the shared `ConfirmDialog`
(`components/ui/ConfirmDialog.tsx` — base-ui AlertDialog, `role="alertdialog"`,
controlled, destructive tone for Cancel). `OrderDetailPage` branches Fulfil/Cancel
visibility on `Shop.Orders.Manage` (view-only members see NO action controls at
all, not disabled ones), and offers no action from a terminal status
(`Fulfilled`/`Cancelled`). The idempotency key is a stable UUID per (order,
action): a re-click of the same unchanged action reuses it, a different action
mints a fresh one; a sync `submittingRef` guard blocks a double fire. Status is
rendered only after the mutation resolves (never optimistic) — the panel re-reads
through the `orders` slot, whose mock shares the same in-memory `SeedOrder[]`
via `findSeedOrder`. The 409 `reason` maps to its own visible message
(`stale_version` offers a re-read; `invalid_order_transition` does not); the
dev-only `DevOrderOperationsScenarioSwitcher` (sessionStorage
`tfOrderOperationsScenario`) forces `stale_version` / `invalid_order_transition` /
unavailable, and a `view-only` evidence pass strips `Shop.Orders.Manage` from
`/me/permissions` by route interception because no seeded non-owner member
resolves it.

The `payments` slot (B044/F052) is the gateway-neutral payment lifecycle. It
feeds three storefront routes: `PaymentRedirectPage` (`payment-redirect` — calls
`initiate` with a mount-stable idempotency key, vets the returned `redirectUrl`
via `assertAllowedPaymentRedirect` in `contracts/paymentLifecycleContract.ts`,
then navigates only if the check passes), `SandboxBankPage` (`bank` — the in-app
Sandbox, gated on `import.meta.env.DEV` so it is absent from production) and
`PaymentResultPage` (`payment-result` — resolves PURELY from the opaque
`resultToken` in the URL, so a refresh is safe; `PendingPayment` is bounded
polling, not an error). The token + order travel between the redirect and bank
pages through the per-tenant `paymentFlowToken` sessionStorage carrier
(`tenantforge:shop:paymentFlow`), which the bank clears after handing the token
to the result page. The mock (`mockShopPaymentsClient`) seeds its own 4 demo
orders for `0RN590ZYXKNZ2` only (foreign tenant → same non-leaking `404`),
persists attempt/idempotency state to sessionStorage (`tfPaymentsMockStore`) so
a same-tab refresh still resolves, and models B044 exactly: one live attempt per
order, a same-key replay is byte-identical, and EVERY status miss (blank / wrong
/ foreign / no-attempt) is the one identical generic `404` — there is no
separate "empty" shape. `resolveSandbox` is optional on the port
(Development-only). F062 swaps the binding for HTTP and lifts the redirect
helper into a shared `assertAllowedPaymentRedirect`.

### Shop 429 cooldown (S42/B046, F053)

`features/shop/rateLimitScenario.ts` is the ONE dev-only rate-limit scenario
module (sessionStorage `tfRateLimitScenario`; scenarios `off` | `lookup` |
`cart` | `checkout` | `order` | `payment`). Everything is gated on
`import.meta.env.DEV` and is a no-op in production: the page-level throttle gate
`assertNotRateLimited(action)` throws the B046 429 (`buildRateLimitedError`,
type `shop_rate_limit`, `retryAfterSeconds: 1`) BEFORE the request when the
scenario targets that action, and `mockShopCartLeaseClient` (add/update/remove)
and `mockShopPaymentsClient.initiate` throw it from the mock. The five pages
(OrderTracking/Cart/Checkout/OrderReview/PaymentRedirect) catch a 429 from
either source with `isRateLimitedProblem(error.problem)` and drive the shared
`useRateLimitCooldown` hook: it disables ONLY the triggering action for
`retryAfterSeconds`, shows the `RateLimitCountdown` chip (warning tone,
`dir="ltr"` tabular number, renders nothing at 0), never auto-submits at 0, and
cleans its interval on unmount. Contract: `rateLimitedProblemSchema` in
`shopContract.ts` extends `shopProblemSchema` (`shopProblemSchema`/
`ShopClientError` untouched); the ONE shared parser
(`shopFetch.ts` `failed()`) folds the `Retry-After` response header into
`retryAfterSeconds` — do not add a second parser.

The `coupons` slot (B041/F049) feeds the admin `CouponsPage` (create/edit/
deactivate, null-rendered-as-«نامحدود», usage `redeemedCount`/`redemptionLimit`,
distinct expired vs inactive, code+type locked when `redeemedCount > 0`,
optimistic `expectedVersion` → `409 stale_version`) and the checkout coupon
preview in `CheckoutPage`. The preview is a pure, read-only mirror of B041's
`ShopCouponPolicy.Evaluate` (`evaluateCouponPreview` + `findCouponByCode` in
`contracts/couponRulesContract.ts`): it maps each of the five reason codes
(`coupon_not_found`/`inactive`/`expired`/`minimum_not_met`/`limit_reached`) to
its own message and NEVER writes `redeemedCount`, so a preview can't decrease
usage. Mixed-phase seam to preserve: with a coupon code typed, `CheckoutPage`
skips the real `fetchCheckoutSummary` (the mock-minted cart 404s the real API)
and the mock verdict owns the coupon line; F059 restores the single server-side
summary. The B041 `Coupon` wire type in `couponRulesContract.ts` is distinct from
the older pre-B041 admin `Coupon` in `shopCheckoutAdminTypes.ts` (the F033/B029
shape, still consumed by `shippingAndCouponAdapter`) — different endpoints, not a
duplicate.

The `profile` slot (B039/F047) is the first client that also feeds the
**anonymous storefront layout**: `StorefrontLayout` and the five `PolicyPage`
routes consume `profile.getPublic`, while the admin `ShopProfilePage` consumes
`getAdmin`/`save`. `getPublic` resolves to `null` for a missing or unpublished
profile (the anonymous 404) — the layout and every policy page render the SAME
neutral "not yet open" fallback for that null, so real content and the fallback
never mix on different surfaces. Publication gates only the profile/policy
surfaces, never the catalog (B039), so the catalog, category bar and cart keep
working for an unpublished store.

The `cartLease` slot (B040/F048) owns the reservation UX on the cart, checkout
and order-review pages through the shared `useCartLease` hook (one recovery
behavior on all three) plus `CartLeaseCountdown` (display-only tick, never
polls or auto-extends) and `CartLeaseRecovery` (the shared 410 panel) in
`features/shop/CartLeaseUi.tsx`. Two mixed-phase seams to preserve:
- `StorefrontLayout`'s header cart badge reads the STORED cart id through the
  mock `cartLease.getCart` — read-only, it never mints an id (F033 semantic).
  F048 moved it off the real `cartAdapter`: the real adapter's 404 handler
  (`clearCartId`) wiped the client-minted mock id and raced the lease, breaking
  the cart→checkout SPA flow and 404ing the real API in the mock phase.
- The product-detail "add to cart" button still routes through the REAL
  `cartAdapter.addItem` (F033), so an add there 404s the mock's cart — expected
  mixed-phase noise, out of F048 scope. The `cartLease.addItem` contract is
  proven at the client level (F058 binds HTTP and restores the PDP path).
`useCartLease` mints a stored cart id via `getOrCreateCartId` when none exists
(mock-phase demonstrability); the real B028 flow creates the cart on
add-to-cart and F058 restores that exact behavior. Expiry clears ONLY the
current tenant's `cartStorage` entry + `orderDraftState` entry; both files are
per-tenant records under one shared key.

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
  `SHOP_SHIPPING_MANAGE_KEY = 'Shop.Shipping.Manage'`,
  `SHOP_SETTINGS_MANAGE_KEY = 'Shop.Settings.Manage'`,
  `SHOP_ORDERS_VIEW_KEY = 'Shop.Orders.View'`,
  `SHOP_ORDERS_MANAGE_KEY = 'Shop.Orders.Manage'`). Adding a key means updating
  **both** files, and the backend must already expose it.
- The mirror is **fail-closed and whole-set**: `roleAdapter.parseCurrentPermissions`
  rejects the *entire* resolved set (and the server catalog parse rejects the whole
  matrix) if **any one** key is missing from `CATALOG_KEY_ORDER`. A single un-mirrored
  server key therefore blanks every permission-gated page and nav item to denied.
  Keep the mirror in lock-step with the live server catalog
  (`GET /api/permissions/catalog`) — before adding a new key, check the catalog for
  any other un-mirrored key and add them together. (F050 hit exactly this: the server
  resolves `Shop.Settings.Manage` for the demo owner but the mirror lacked it, so the
  new orders page rendered denied until the key was added.)
- The `هویت و سیاست‌های فروشگاه` nav item (F047) is still un-gated: `ShopProfilePage`
  gates nothing, and F057 binds it to `Shop.Settings.Manage` (the key now exists in the
  mirror). The page still surfaces the client's 403 state.
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
    fixed` (each a `z-50` fixed overlay at the viewport bottom). On narrow
    viewports (390px mobile) they stack up and hit-test over content, so
    Playwright pointer clicks — even `force: true` — are reported as "intercepts
    pointer events" because `force` skips the stability check but NOT the
    hit-target check. A hide that runs once on the PREVIOUS page does not stick:
    the overlay remounts on the next page load and re-asserts. The reliable
    fixes are to (a) set `display:none` on the `[aria-label*="فقط توسعه"]`
    overlays in the SAME document, immediately before the click, or (b) dispatch
    a DOM `el.click()` inside `evaluate()` (bypasses hit-testing entirely).
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
12. Admin settings pages that `form.reset()` a just-loaded profile (e.g.
    `ShopProfilePage`, F047) race a Playwright `page.fill()`: the form renders
    the instant the skeleton resolves, but the reset that seeds the loaded
    values lands a frame later and clobbers a fill issued in that window (the
    value reverts to the seed). In a browser-evidence script, first wait until
    the name field holds the seeded value (proof the reset ran), then fill, and
    re-fill once if the value does not stick.
 13. When browser evidence hits a PROTECTED page with a real session, the URL's
    `tenantId` must be a tenant the signed-in account actually belongs to, or
    the page's tenant-scope `GET /api/tenants/{id}/me/permissions` 403s and the
    console is no longer clean. For **permission-gated** pages (F050 orders) the
    member tenant must also *resolve* the required key, or the page renders the
    denied panel regardless of the mock. The dev admin belongs to
    `0RN590ZYXKNZ2` (F029 Demo Boutique), which resolves the full Shop set incl.
    `Shop.Orders.View`; `0RM4B8A9M008Q` 403s for the admin (not a member). Use
    `0RN590ZYXKNZ2` for the orders pages.
14. Playwright's `console` event reports a failed resource as
    `"Failed to load resource: the server responded with a status of 404 (Not
    Found)"` with NO URL, so filtering console text can never tell a real 404
    from expected mixed-phase noise (F031 catalog/search/PDP still call the
    real API for demo slugs). In evidence scripts, record failed URLs on the
    `response` event and whitelist them there (`/api/shop/<demo-slug>/…`);
     drop the URL-less console lines and judge the response list instead.
 15. Mock Shop clients must model **tenant isolation**, not just per-tenant
     seeding: a capability whose backend scopes reads to the route tenant (B042
     orders) must return the SAME non-leaking `404`/empty for a *foreign* tenant
     scope as for a missing id. Seed only the demo tenant
     (`DEMO_TENANT_ID = '0RN590ZYXKNZ2'`); any other tenant gets an empty store.
     (F050's mock initially seeded every tenant, so a foreign-tenant order id
     wrongly 200'd.) A **non-member** tenant is a different path: the page's
     `useTenantPermissions` gate renders the denied panel before any fetch, so a
     foreign-tenant *route* shows denied while a foreign order id *within* the
      current tenant's scope shows the 404.
 16. Browser evidence that walks the storefront cart → checkout → order-review
     flow must pin the cart-lease scenario to `normalLease`
     (sessionStorage `tfCartLeaseScenario`) in a context init script BEFORE any
     document loads: the mock's module-level default is the 30-second
     `shortLease` (the F048 expiry demo), which expires the seeded cart
     mid-flow and turns order review into the 410 recovery panel. The cart
     seed itself is keyed by the stored localStorage cart id
     (`tenantforge:shop:cartId`), so evidence must seed ONE stable id and never
     replace it (a fresh id per call reads back as a non-leaking 404).
 17. The guest order-tracking route is `track-order` (App.tsx), not
     `order-tracking`. The storefront 429 pages have no loading/empty states of
     their own beyond what F039–F052 built; F053 only adds the per-action
     cooldown, so its evidence asserts the 429 behaviors plus the existing
     states.


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
- `docs/user-guide/shop-quickstart.md` — English Shop quickstart; its
  real-vs-mock table (section 2) is the consolidated, human-readable snapshot of
  which Shop slot is mock-backed today. When a connect task (F054–F063) flips a
  slot from mock to HTTP, update that table in the same task.

## Maintaining this file

Update it in the same task that changes a fact above. Add durable facts only:
no debugging notes, no speculation, no abandoned approaches, no secrets, no
large code blocks. If a task changed nothing here, say so in the completion
report instead of padding the file.
