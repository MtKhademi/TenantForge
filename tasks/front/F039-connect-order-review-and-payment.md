---
id: F039
slice: S29
title: Connect order review and payment to the real API
agent: ui-engineer
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Replace F038's mocked order-review/sandbox-payment flow with real calls
to B031's order-creation API and B032's sandbox-payment API.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/029-shop-order-and-sandbox-payment.md`. Read
B031's and B032's delivered endpoint shapes before writing the real
adapters.

# Scope

1. On "place order," call `POST /api/shop/{tenantId}/orders` with the
   cart id (from `cartStorage.ts`) and the checkout inputs already
   collected (F037); on success, call the initiate-payment endpoint and
   navigate to the sandbox bank page using the returned redirect target.
2. On the sandbox bank page's Approve/Decline actions, call
   `POST /api/shop/{tenantId}/orders/{orderId}/payments/callback` with
   `approved: true`/`false` and navigate to the result page based on the
   response.
3. On a successful order, clear the stored cart id (`cartStorage.ts`) so
   a fresh cart starts on the next visit — the real cart is consumed
   server-side by B031; the client must not keep pointing at it.
4. Remove the mock data path entirely.

# Non-goals

- No admin order-management screen.
- No email/SMS confirmation UI.

# Acceptance

- Placing a real order creates it via B031, initiates payment via B032,
  and the approve/decline actions correctly reach `Paid`/still-pending
  outcomes.
- The stored cart id is cleared after a successful order, and a
  subsequent add-to-cart starts a fresh cart.
- A stock-race failure at order-creation time (a variant sold out between
  checkout summary and order creation) shows a clear error, not a broken
  page.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Complete a real checkout into a real order, approve on the sandbox
  bank page, and confirm the order shows `Paid` with a real order
  number/tracking code; repeat and decline, and confirm the order stays
  pending with a working retry.
- No new browser console error.

# Lifecycle

Add row `F039` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F038, B031, B032`, and Spec link
`tasks/front/F039-connect-order-review-and-payment.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
