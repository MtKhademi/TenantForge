---
id: B033
slice: S30
title: Order lookup API
agent: backend-mentor
source: tasks/slices/030-guest-order-tracking.md
---

# Objective

Add `POST /api/shop/{tenantId}/orders/lookup`: an unauthenticated
endpoint that requires both a tracking code and a phone number together
(never tracking code alone) and returns that order's current status,
items and totals when the pair matches, or a plain, generic not-found
response otherwise.

# Context

Read `tasks/slices/030-guest-order-tracking.md` completely, in particular
its explicit security reasoning for requiring both values together and
for returning one generic not-found response regardless of which half was
wrong. Read the already-delivered `domain/ShopOrder.cs`/`ShopOrderItem.cs`
(B031) — this endpoint reads them, it defines no new table.

# Scope

`features/orders/OrderLookupFeature.cs`:

`POST /api/shop/{tenantId}/orders/lookup` — anonymous. Body:
`trackingCode`, `customerPhone`. Both required (missing either →
`Results.ValidationProblem`, a normal input-validation response — this is
distinct from the "not found" case below, which covers a well-formed
request that simply does not match a real order).

1. Query `ShopOrder` for this tenant where `TrackingCode == trackingCode`
   and `CustomerPhone == customerPhone` (exact match on both).
2. No match → return one generic `404` (e.g. `{ "detail": "No order was
   found for this tracking code and phone number." }`) — the same body
   whether the tracking code exists with a different phone, or does not
   exist at all. Do not branch response shape/status on which half
   failed.
3. Match → return `Status`, `OrderNumber`, `CreatedAtUtc`, the shipping
   address fields, `SubTotal`/`ShippingCost`/`DiscountAmount`/`GrandTotal`,
   and every `ShopOrderItem` (`ProductNameSnapshot`, `VariantLabelSnapshot`,
   `UnitPrice`, `Quantity`) — the snapshot fields, never a live join back
   to the current `ShopProduct`/`ShopProductVariant` rows, so a later
   catalog edit never changes what a past order shows.

# Non-goals

- No account/login of any kind.
- No rate-limiting/CAPTCHA — restated from the slice as an explicitly
  deferred hardening step, not an oversight.
- No order mutation (cancel, edit address, etc.) from this endpoint — it
  is read-only.

# Acceptance

- A correct tracking-code + phone pair returns the order's status, items
  (from the snapshot fields) and totals.
- A real tracking code with a mismatched phone number returns the exact
  same generic not-found response as a wholly nonexistent tracking code
  (proven by an integration test asserting identical status code and body
  shape for both cases).
- A request missing either field returns a normal validation error,
  distinct from the not-found response.
- The response never includes a live catalog lookup — verified by an
  integration test that edits the product's name via B026's admin API
  after the order was placed and confirms the lookup response still shows
  the original `ProductNameSnapshot`.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the happy-path lookup, the mismatched-phone
vs. nonexistent-code identical-response proof, the missing-field
validation error, and the snapshot-isolation proof described above. The
full existing IAM suite continues to pass unmodified.

Manual:

- Place an order (B031/B032), then look it up with the correct tracking
  code and phone; try a wrong phone number for the same code and confirm
  the identical generic not-found response.

# Lifecycle

Add row `B033` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B032`, and Spec link
`tasks/backend/B033-guest-order-lookup-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/030-guest-order-tracking.md` is the permanent
record and is never deleted.
