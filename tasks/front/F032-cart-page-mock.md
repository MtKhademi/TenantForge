---
id: F032
slice: S27
title: Cart page mock
agent: ui-engineer
source: tasks/slices/027-shop-cart.md
---

# Objective

Build the storefront cart page: an item list (thumbnail, color/size
label, quantity stepper, remove action), a computed subtotal, a "proceed
to checkout" action and an empty-cart state — against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/027-shop-cart.md`. This page lives inside the same
unauthenticated `StorefrontLayout` F030 built — reuse it, do not create a
second public layout.

# Scope

1. `src/web/src/pages/shop/storefront/CartPage.tsx`: reads the mocked
   cart state F030's add-to-cart interaction already populates (extend
   that mock module rather than starting a second, disconnected mock
   cart).
2. Each line item shows a thumbnail, product name, color/size label, a
   quantity stepper (bounded by the mocked variant's stock, matching
   F030's stepper behavior) and a remove action.
3. A subtotal computed from the mocked items, and a "proceed to
   checkout" primary action (links to a placeholder checkout route —
   F036 builds the real checkout page).
4. An empty-cart state (no items) with a clear call-to-action back to the
   storefront.

# Non-goals

- No connection to a real API (F033 connects this page once B028 exists).
- No checkout page itself (F036).

# Acceptance

- The cart page reflects the same mocked cart state F030's add-to-cart
  action populates, with working quantity change and remove actions and a
  correct subtotal.
- The empty-cart state renders correctly when no items are present.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Add an item from the product detail page, open the cart, change its
  quantity, remove it, and confirm the empty state appears; add it back
  and confirm the subtotal recomputes correctly.
- No browser console error.

# Lifecycle

Add row `F032` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F030`, and Spec link
`tasks/front/F032-cart-page-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
