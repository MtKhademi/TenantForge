---
id: F038
slice: S29
title: Order review and sandbox payment mock
agent: ui-engineer
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Build an order-review/summary screen, the in-app fake "bank page"
(approve/decline buttons) the Sandbox payment provider redirects to, and
a payment-result screen — against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/029-shop-order-and-sandbox-payment.md`, in
particular its statement that the "bank page" is entirely an in-app
frontend screen (no real bank, no external redirect) standing in for a
real gateway's hosted payment page. Make its visual language deliberately
distinct from the rest of TenantForge's storefront (a plain, obviously
"simulation" bank-page treatment — for example a clearly labeled "Sandbox
Bank" header) so nobody mistakes it for a real payment provider's UI.

# Scope

1. `src/web/src/pages/shop/storefront/OrderReviewPage.tsx`: shows the
   about-to-be-created order's items, address and totals (from F037's
   already-connected checkout summary state) and a "place order" action
   that, for this mock task, navigates to the mocked bank page.
2. `src/web/src/pages/shop/storefront/SandboxBankPage.tsx`: a clearly
   labeled simulation page showing the order's amount and two actions,
   "Approve" and "Decline."
3. `src/web/src/pages/shop/storefront/PaymentResultPage.tsx`: shows
   success (order paid, order number/tracking code) or failure (order
   still pending, a retry action back to the bank page) depending on the
   mocked choice made on the bank page.

# Non-goals

- No connection to a real API (F039 connects this flow once B031/B032
  exist).
- No real payment gateway UI/branding anywhere — the Sandbox page must
  never resemble a real bank's page.

# Acceptance

- The review → bank page → result flow works fully against mocked data
  for both the approve and decline paths.
- The bank page is unmistakably a simulation, not a real payment
  provider's UI.
- The result page shows the order number and tracking code on success.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Walk through review → approve → success result, then review → decline
  → failure result with a retry action back to the bank page.
- No browser console error.

# Lifecycle

Add row `F038` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F037`, and Spec link
`tasks/front/F038-order-review-and-sandbox-payment-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
