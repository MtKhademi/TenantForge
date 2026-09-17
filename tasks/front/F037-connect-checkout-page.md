---
id: F037
slice: S28
title: Connect checkout page to the real API
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Replace F036's mocked checkout summary computation with real calls to
B030's checkout API.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. Read B030's delivered
endpoint shape before writing the real adapter.

# Scope

1. Replace F036's mocked summary computation with a real call to
   `POST /api/shop/{tenantId}/checkout/summary`, sending the stored cart
   id (from `cartStorage.ts`, F033) plus the address/coupon fields,
   recomputed on every relevant field change (debounced if needed to
   avoid excessive calls).
2. Map B030's distinct validation errors (unshippable province, invalid
   coupon) to F036's existing clear-message presentation.
3. Remove the mock computation path entirely.

# Non-goals

- No order creation or payment (F038/F039).

# Acceptance

- The summary reflects the real API's computed totals as the shopper
  edits the address/coupon fields.
- A real unshippable province and a real invalid/expired coupon both show
  the API's distinct error messages.
- No mock computation path remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- With a real cart (from F033) and real shipping rates/coupons (from
  F035), fill in a shippable province with a valid coupon and confirm
  the correct totals; try an unshippable province and an invalid coupon
  and confirm the clear errors.
- No new browser console error.

# Lifecycle

Add row `F037` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F036, B030`, and Spec link
`tasks/front/F037-connect-checkout-page.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
