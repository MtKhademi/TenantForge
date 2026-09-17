---
id: F033
slice: S27
title: Connect cart page to the real API
agent: ui-engineer
source: tasks/slices/027-shop-cart.md
---

# Objective

Replace the mocked cart state F030/F032 built with real calls to B028's
cart API, including persisting the cart id in the browser across page
loads.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/027-shop-cart.md`, in particular its storage-
convention reasoning: read `src/web/src/features/auth/httpAuthAdapter.ts`
and `AuthContext.tsx` for the existing client-side-credential adapter
shape (a small typed module, `sessionStorage`-backed, JSON-serialized,
read once at bootstrap) before writing the cart-id adapter. Follow that
same adapter shape but back it with `localStorage`, not `sessionStorage`
— the slice document states this deliberately: a session should end when
the tab closes, a shopping cart should not.

# Scope

1. `src/web/src/features/shop/cartStorage.ts`: a small typed adapter
   (`getCartId`, `setCartId`, `clearCartId`) around `localStorage`,
   guarded by try/catch exactly like the design system's existing
   browser-storage guidance, mirroring `httpAuthAdapter.ts`'s shape.
2. Replace F030's/F032's mocked cart state with real calls to
   `/api/shop/{tenantId}/carts` (create on first add-to-cart if no cart
   id is stored yet), `/api/shop/{tenantId}/carts/{cartId}/items`
   (add/update/remove) and `/api/shop/{tenantId}/carts/{cartId}` (fetch),
   using the stored cart id from `cartStorage.ts`.
3. On add-to-cart from the product detail page (F031's connected page):
   if no cart id is stored, create one first, then add the item; if one
   is stored, add directly. If the stored cart id is rejected by the API
   (e.g. it no longer exists — for example after a later task clears it
   post-order), clear it and create a fresh cart transparently rather
   than showing a raw error.
4. Remove the mock cart module entirely.

# Non-goals

- No checkout page (F036).
- No change to the storefront browsing pages beyond wiring the
  add-to-cart action to the real cart adapter.

# Acceptance

- Adding an item from the product detail page creates a real cart (first
  time) or adds to the existing one, persisted via `localStorage` and
  surviving a full page reload/browser restart within the same browser
  profile.
- Cart quantity changes and removals call the real API and the displayed
  subtotal matches the API's computed value.
- A stock-insufficient add/update attempt shows the API's clear error,
  not a silent no-op.
- No mock cart state remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Add an item, reload the page, and confirm the cart still shows the
  item (proving `localStorage` persistence and the real API, not
  component state). Change quantity beyond available stock and confirm
  the clear rejection.
- No new browser console error.

# Lifecycle

Add row `F033` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F032, B028`, and Spec link
`tasks/front/F033-connect-cart-page.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
