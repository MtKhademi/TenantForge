---
id: F035
slice: S28
title: Connect admin shipping-rate and coupon management to the real API
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Replace F034's mocked shipping-rate and coupon admin screens with real
calls to B029's API.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. Read B029's delivered
endpoint shapes before writing the real adapter.

# Scope

1. Add a real HTTP adapter calling
   `/api/tenants/{tenantId}/shop/shipping-rates` and
   `/api/tenants/{tenantId}/shop/coupons` (create/list/deactivate).
2. Replace F034's mocked data source with this adapter.
3. Map B029's validation errors (duplicate coupon code, invalid discount
   value) to F034's existing inline error presentation.
4. Remove the mock data path entirely.

# Non-goals

- No new screen or field beyond what F034 already built.

# Acceptance

- Shipping-rate set/update and coupon create/list/deactivate all work
  end-to-end against the real API.
- A duplicate coupon code shows a clear inline error.
- No mock data path remains reachable.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Set a real shipping rate and coupon, reload the page, and confirm both
  persist. Attempt a duplicate coupon code and confirm the clear error.
- No new browser console error.

# Lifecycle

Add row `F035` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F034, B029`, and Spec link
`tasks/front/F035-connect-admin-shipping-and-coupon.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
