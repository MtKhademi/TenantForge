# S30 — Guest order tracking

## Outcome

A guest customer who placed an order in S29 (no account, by design — see
S27's Non-goals) can come back later, enter their tracking code and phone
number together, and see their order's current status, items and totals.
This closes the last piece of darnoshop's guest-first flow: order,
payment, then a way to check on it without ever creating an account.

## Route addition (reusing S26's convention)

- `POST /api/shop/{tenantId}/orders/lookup` (anonymous) — body carries
  `trackingCode` and `customerPhone`, both required together. A `POST`
  with both values in the body (not a `GET` with them as query
  parameters) is the deliberate choice here: a tracking code plus a phone
  number is exactly the kind of pair that should not sit in server access
  logs, browser history or a shareable URL.

## Scope

**B033 — depends on B032:**
- `POST /api/shop/{tenantId}/orders/lookup`: requires **both**
  `trackingCode` and `customerPhone` — tracking code alone is never
  sufficient. This is a deliberate, named security decision: `TrackingCode`
  is a short, human-typed string (not a full TSID), so accepting it alone
  would let anyone scan/guess their way into other customers' order
  details; pairing it with the phone number the order was actually placed
  under closes that enumeration path while still needing no account.
  Returns the order's status, items (from the `ShopOrderItem` snapshot —
  never the live product/variant, so a later catalog edit never changes
  what a past order shows) and totals when the pair matches a real order
  for that tenant; returns a plain, generic "not found" for any mismatch
  (never a distinct "wrong phone" vs. "wrong tracking code" message, which
  itself would leak which half was correct).

**F040 — depends on F039:**
- Order tracking page mock: a tracking-code + phone number form and an
  order-status result view (status, items, totals). Mocked data.

**F041 — depends on F040, B033:**
- Connect the order tracking page to B033's real API.

## Non-goals

- No account/login of any kind is introduced to make tracking "easier" —
  the tracking-code + phone pairing is the entire, deliberately guest-only
  mechanism, matching every other Shop endpoint's guest-first design
  (S27's Non-goals).
- No order cancellation, modification or re-order action from the tracking
  page — it is read-only.
- No rate-limiting/CAPTCHA hardening beyond requiring both values together;
  a stronger anti-enumeration control is a separate, later task if a real
  need for it is demonstrated.

## Verification

- `dotnet build TenantForge.sln --nologo` and the full integration suite
  pass after B033, including: a correct tracking-code+phone pair
  returning the order, a mismatched phone for a real tracking code
  returning the same generic not-found response as a wholly made-up code
  (proving no information leak between the two failure cases).
- `npm run build` and `npm run lint` in `src/web/` pass after F040/F041.
- Real browser demo at 1440×900 and 390×844: look up a real order placed
  earlier in this manual test pass and confirm its status/items/totals
  display; try a wrong phone number for that same tracking code and
  confirm the plain not-found message.
- No new browser console error.
