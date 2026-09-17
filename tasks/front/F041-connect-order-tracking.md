---
id: F041
slice: S30
title: Connect order tracking page to the real API
agent: ui-engineer
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Replace F040's mocked order-tracking lookup with a real call to B033's
order lookup API.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md`. Read the
complete `tasks/slices/030-guest-order-tracking.md`. Read B033's
delivered endpoint shape before writing the real adapter.

# Scope

1. Replace F040's mocked lookup with a real call to
   `POST /api/shop/{tenantId}/orders/lookup`, sending `trackingCode` and
   `customerPhone`.
2. Present the API's one generic not-found response exactly as F040
   already designed it (never branching UI copy on which field the API
   might have rejected — the API itself never distinguishes, so the UI
   has nothing to branch on).
3. Remove the mock data path entirely.

# Non-goals

- No order-modification action.

# Acceptance

- A real, correct tracking-code + phone pair shows the real order's
  status/items/totals.
- A real tracking code with a wrong phone number, and a wholly made-up
  tracking code, both show the identical generic not-found message.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900 and 390×844:

- Look up a real order placed earlier (F039) with its correct tracking
  code and phone, then with a wrong phone number for the same code, and
  confirm the two outcomes described above.
- No new browser console error.

# Lifecycle

Add row `F041` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependencies `F040, B033`, and Spec link
`tasks/front/F041-connect-order-tracking.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
