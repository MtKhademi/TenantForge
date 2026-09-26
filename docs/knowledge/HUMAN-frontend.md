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
- Shop administration: categories, products, product galleries, shipping rates and
  coupons;
- a Shop storefront: an all-products catalog with search, category and sale
  filters, four sort orders and pagination (still backed by the F045 mock),
  category browsing with product thumbnails, product detail galleries, cart,
  checkout, order review, a sandbox bank page, a payment result page and guest
  order tracking;
- one level of subcategories: the admin category page shows each root with its
  direct children indented beneath it and lets you create, edit and deactivate
  both levels; the storefront groups each root's children under it in the
  navigation bar and shows at most two breadcrumb levels (root, then child).
  This is still backed by the F046 mock, and the data model deliberately has no
  third level;
- store identity and policies: an admin "identity and policies" settings form
  (name, tagline, support phone, Instagram, and the five customer-facing
  texts — about, shipping, payment, returns, privacy) with live character
  counters, a published toggle and a live storefront preview; the storefront
  header and footer show the store's name, tagline, phone and Instagram, and
  five policy pages render the stored text as plain text with line breaks
  preserved. This is still backed by the F047 mock (connected in F057);
- cart reservation expiry: the cart, checkout and order-review pages show a
  live "reservation expires in" countdown. Adding, changing or removing an
  item pushes the deadline out; merely opening a page never does. When the
  reservation runs out, all three pages swap to the same recovery panel with a
  "back to the store" action, and only that store's saved cart and order draft
  are cleared — a second store's cart in the same browser stays intact. The
   header's cart count now follows the same reservation-aware data path. This is
   still backed by the F048 mock (connected in F058).
- advanced coupon rules: the admin coupon page now supports minimum subtotal, a
   maximum-discount cap and a redemption limit alongside the original code and
   discount. A blank cap or limit means "unlimited" and is shown as «نامحدود»,
   never an empty cell. Each row shows its usage (how many times redeemed versus
   its limit), and a coupon that has hit its limit is highlighted as exhausted.
   Expired and inactive coupons are styled differently from an active one. Once a
   coupon has been redeemed at least once, its code and discount type are locked
   so they cannot change; everything else stays editable. Editing is protected by
   an optimistic version check — if the coupon changed behind your back, you get a
   clear "reload and try again" conflict instead of a silent overwrite. Money
   inputs carry an explicit unit (toman / percent) and accept Persian digits,
   which are converted to standard digits before anything is validated or sent.
   At checkout, a typed coupon is checked against the live cart and every reason
    the store can reject it (unknown, inactive, expired, below the minimum, limit
    reached) shows its own distinct message, and a preview never changes the
    redemption count — only a real order would. This is still backed by the F049
    mock (connected in F059).
 - shop order review: a permission-gated "orders" list and a read-only order
   detail. The list shows order number, customer, phone, status, total and
   created date, and filters by search (order number, tracking code, customer
   name or phone), status and a date range; every filter is kept in the URL so a
   filtered view survives a reload and the browser back/forward button moves
   between filter states. It paginates, renders as a table on a wide screen and
   as cards on a phone, and shows the right state for empty, an invalid filter
   (400), no permission (403) and an unreachable request (with retry). The detail
    shows the order as it was captured at purchase — the item's stored name,
    variant and price (later product changes do not alter it) — alongside the
    customer, the shipping address, the totals, and the payment-attempt history
    limited to the 20 newest attempts. A malformed, missing or other-tenant order
    id all show the same generic "order not found". This is still backed by the
    F050 mock (connected in F060).
  - shop order operations: on that same order detail, a member who can manage
    orders sees an "order operations" region. A paid order offers "fulfil the
    order" and a pending-payment order offers "cancel the order"; both are
    irreversible, so the button opens a confirmation dialog (fully keyboard-
    operable, Escape cancels without sending anything) before the action is
    sent. The status badge only changes after the action really succeeds — the
    page never shows the new status optimistically — and the order version
    bumps accordingly. If the order changed in the meantime, a conflict message
    with a "reload" action appears; if the order is no longer in a state that
    allows the action, a separate message explains the invalid transition.
    Members who can only view orders see no action controls at all. This is
     still backed by the F051 mock (connected in F061).
  - shop payment lifecycle: the gateway-neutral payment flow. After an order is
    placed the checkout hands off to a "prepare payment" page that starts one
    payment attempt and — only after vetting the gateway's redirect target for
    an unsafe scheme (http), an unexpected host, or a protocol-relative
    `//host` — navigates to it. Each unsafe shape gets its own clear message and
    never navigates. In development a sandbox "bank" page stands in for the real
    gateway: approve or decline, and the outcome is driven by the server's
    resolved status, not by which button you pressed (the server can reject an
    approval). A payment result page then shows the outcome and, while the
    order is still pending, re-checks a bounded number of times before stopping
    and offering a manual "check again" — it never polls forever. Because the
    result is read from the opaque payment token alone, refreshing the page
    after the redirect still shows the correct state. This is still backed by
    the F052 mock (connected in F062).

Live status for everything else is in `tasks/TASKS.md`.

## Decisions worth knowing, and why

**The backend contract is the source of truth.** The frontend never defines its
own permanent shape for server data. When a screen has to ship before its API
exists, the mock is written against the agreed backend contract — same field
names, nullability and error codes — so connecting it later replaces one
adapter and touches no component.

**Pages never call `fetch`.** All network access goes through a feature
adapter or Shop client. That is what makes mock-first delivery possible and
keeps error handling, timeouts and token attachment in one place per capability.

**New Shop capabilities use a client provider.** Each Shop capability exposes a
client interface that its mock and later HTTP implementation both satisfy. The
provider binds one slot per capability — `media` (product galleries),
`discovery` (the all-products catalog), `categories` (the category hierarchy),
`profile` (store identity and policies), `cartLease` (cart reservation expiry),
`coupons` (advanced coupon rules), `orders` (order review),
`orderOperations` (fulfil and cancel an order) and `payments` (the gateway-
neutral payment lifecycle) are bound today, each to its mock client.
Screens consume `useShopClients()`; connecting a real API later
replaces exactly one provider slot and never touches the screen. The order list's filter state
lives in the URL, so a filtered view is refreshable and back/forward-
reproducible.

**Permission keys are mirrored from the server, fail-closed.** The app keeps a
small local list of the permission keys it knows how to branch on. The server is
the authority and this list is a guard, not a substitute: if the server ever
reports a key the local list does not contain, the app treats the whole
permission set as unknown and every permission-gated screen and nav item safely
collapses to its "not allowed" state instead of guessing. When a new backend
permission is introduced, that key is added to this local list in the same work
that first branches on it (F050 added `Shop.Orders.View`, `Shop.Orders.Manage`
and the long-missing `Shop.Settings.Manage`).

**Publishing a store only hides its identity, not its goods.** The "publish"
toggle controls whether the storefront shows the store's name, tagline, contact
details and policy pages. An unpublished (or not-yet-created) store shows one
neutral "this store is not yet open" state in the header, the footer and on
every policy page — the same fallback everywhere, never real content on one
surface and a placeholder on another. The product catalog, cart and checkout
keep working regardless, so existing storefront URLs stay usable during rollout.

**Categories are deliberately two levels deep.** A category is either a root or
a direct child of a root — there is no grandchild. The admin page is a flat,
two-level list (not a generic tree component) for that reason, and the parent
selector can only ever offer active roots, so the invalid shapes (a third
level, an inactive parent, a parent with children being moved) are answered by
the client with the backend's own error codes rather than by the UI guessing.
"Effective activity" means a child is publicly visible only while both it and
its root are active, so deactivating a root quietly hides its children from the
storefront.

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

**Order actions are never optimistic.** Fulfil and cancel change the status
badge only after the mutation actually succeeds, and each action carries a
stable idempotency key per order-and-action: repeating the same unchanged
action reuses the key (so a double click cannot be mistaken for a new attempt),
while switching to the other action mints a fresh one. A failed action leaves
the previous status and version untouched and shows the backend's reason
instead of guessing.

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
- The payment screens run against a deterministic mock that models the sandbox
  gateway; no real provider is wired up yet.
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
