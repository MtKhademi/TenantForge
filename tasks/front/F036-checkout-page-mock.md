---
id: F036
slice: S28
title: Checkout page mock
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Build the checkout page: an address form (province, city, address line,
postal code), a coupon-code field, and a live order summary (subtotal,
discount, shipping, grand total) that recomputes as the shopper edits the
form — against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. This page lives inside the
storefront's public `StorefrontLayout` (F030), reached from the cart
page's (F032/F033) "proceed to checkout" action.

# Scope

1. `src/web/src/pages/shop/storefront/CheckoutPage.tsx`: an address form
   (province select or free-text field — match whatever the mocked
   province list uses; B030's real API validates against configured
   shipping rates), a coupon-code input, and a summary panel
   (subtotal/discount/shipping/grand total) that recomputes from mocked
   shipping-rate and coupon data as the shopper edits the form.
2. An "unshippable province" mocked case that shows a clear message
   instead of a silently-zero shipping cost, and an "invalid coupon"
   mocked case that shows a clear message instead of silently ignoring
   the code — matching the exact behavior B030's real API will have.
3. A primary action to proceed to order review (F038 builds that page;
   this task links to a placeholder route).

# Non-goals

- No connection to a real API (F037 connects this page once B030 exists).
- No order creation or payment (F038/F039).

# Acceptance

- The summary recomputes correctly as the address/coupon fields change,
  against mocked shipping-rate and coupon data.
- The unshippable-province and invalid-coupon mocked cases both show
  clear, distinct messages, never a silent zero/ignored value.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Fill the address form with a shippable and an unshippable mocked
  province and confirm the summary/error behaves correctly for each;
  enter a valid and an invalid mocked coupon code and confirm the same.
- No browser console error.

# Lifecycle

Add row `F036` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F033`, and Spec link
`tasks/front/F036-checkout-page-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
