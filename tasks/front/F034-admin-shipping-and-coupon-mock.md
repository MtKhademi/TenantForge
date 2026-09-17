---
id: F034
slice: S28
title: Admin shipping-rate and coupon management mock
agent: ui-engineer
source: tasks/slices/028-shop-checkout.md
---

# Objective

Build the admin screens for setting per-province shipping rates and
managing coupons, against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/028-shop-checkout.md`. This page lives inside
`DashboardShell`, alongside the catalog admin pages F028/F029 already
built — reuse the same admin list-page layout and, once delivered,
`StatePanel`.

# Scope

1. `src/web/src/pages/shop/admin/ShippingRatesPage.tsx`: a list of
   configured province/cost rows plus a form to set/update one (province
   picker or free-text field — match whichever the mocked data model
   uses; B029's real API upserts by province name).
2. `src/web/src/pages/shop/admin/CouponsPage.tsx`: a list of coupons
   (code, discount type/value, active state, expiry) plus a create form
   and a deactivate action per row (no edit/reactivate action, matching
   B029's scope).
3. Add both pages to the Shop admin navigation entry F028 already added.

# Non-goals

- No connection to a real API (F035 connects this page once B029 exists).
- No shipping-rate delete UI (B029 has no delete endpoint).

# Acceptance

- Both pages work fully against mocked data, including the coupon
  deactivate action and the shipping-rate upsert-by-province behavior.
- Every required state (idle, loading, empty, validation failure,
  success feedback) is implemented.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Set a shipping rate, update its cost, create a coupon, deactivate it,
  and confirm the list reflects each change.
- No browser console error.

# Lifecycle

Add row `F034` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F029`, and Spec link
`tasks/front/F034-admin-shipping-and-coupon-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
