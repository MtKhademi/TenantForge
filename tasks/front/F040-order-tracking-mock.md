---
id: F040
slice: S30
title: Order tracking page mock
agent: ui-engineer
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Build the guest order-tracking page: a tracking-code + phone-number form
and an order-status result view — against mocked data.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/030-guest-order-tracking.md`, in particular its
rule that both fields are always required together and that a lookup
failure shows one generic message regardless of which field was wrong.

# Scope

1. `src/web/src/pages/shop/storefront/OrderTrackingPage.tsx`: a form with
   a tracking-code field and a phone-number field (both required — submit
   is disabled/validated until both are filled) and, on submit, a result
   view showing the mocked order's status, items and totals, or one
   generic "not found" message for a mocked failure case.
2. Link this page from the storefront layout (F030) so a returning guest
   can reach it without needing an order confirmation email/link.

# Non-goals

- No connection to a real API (F041 connects this page once B033 exists).
- No order-modification action from this page.

# Acceptance

- Submitting a mocked valid pair shows the order's status/items/totals;
  submitting a mocked invalid pair shows the one generic not-found
  message, never a message that reveals which field was wrong.
- Submit is blocked (with a clear validation message) when either field
  is empty.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Submit a mocked valid pair and a mocked invalid pair and confirm the
  two distinct outcomes (result view vs. generic not-found).
- No browser console error.

# Lifecycle

Add row `F040` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F039`, and Spec link
`tasks/front/F040-order-tracking-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
