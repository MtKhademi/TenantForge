# B043 — Fulfil or cancel orders with inventory-safe transitions

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S39`.
- Depends on: `B042, B040`.
- Immediate browser consumer: `F061`. Do not widen this API for an unnamed future screen.
- Visible outcome: An operator marks paid orders fulfilled or cancels unpaid orders; cancellation restores reserved stock exactly once.

## Do this in order

Follow this checklist top to bottom. It restates every requirement below as
literal steps. The prose sections after it are the same requirements in
full — read them too, they are not optional extra scope, just the same
facts written a different way. Before step 1, follow the branch-naming,
ledger-update and approval-gate rules already described in `AGENTS.md` and
in the "Ownership and dependency" section above (do not repeat them here,
just follow them).

1. Read `AGENTS.md`, the `B043` row in `tasks/TASKS.md`, this whole Spec
   file, `docs/modules/SHOP.md` (only if it already exists), and
   `tasks/slices/039-shop-order-operations.md`. Also read the matching
   section of `docs/design/shop/http-contracts.md` — you will update this
   section later, before your Spec file is deleted at delivery. Also read
   `B042`'s delivered contract, since this task builds directly on the
   `Version` field and admin order shapes `B042` added.
2. Confirm the current code still matches this baseline (commit `34dc44e`):
   Shop is one module project, EF entities are internal, IDs are TSIDs
   (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"),
   migration history is tracked in a separate table, IAM membership/role
   checks use raw SQL, storefront/cart/checkout/order/payment/lookup routes
   are anonymous, and integration tests live in files named
   `Shop*IntegrationTests.cs`. Do not change any of these conventions —
   this Spec does not ask you to.
3. Add four new properties to the `ShopOrder` entity: `FulfilledAtUtc`
   (nullable timestamp), `CancelledAtUtc` (nullable timestamp),
   `InventoryReleasedAtUtc` (nullable timestamp), and `Version` (int — skip
   this one if `B042` already added it; otherwise add it here).
4. Add an enum (or equivalent constant set) `OrderStatusAction` with
   exactly two values: `Fulfill` and `Cancel`. Do not add any other value.
5. Add a new entity `ShopOrderOperation` with fields: `TenantId`, `OrderId`,
   an idempotency key (see step 8), the canonical action taken (`Fulfill`
   or `Cancel`), a snapshot of the response that was returned, the actor
   who performed it, and the created timestamp. Add a unique database
   constraint on `(TenantId, Key)` so the same key can never be stored
   twice for the same tenant.
6. Generate one EF migration (EF migration = "a generated script that
   changes the database schema; create it by running
   `dotnet ef migrations add <Name>` inside the Shop module project") that
   adds the four `ShopOrder` columns from step 3 (or three, if `Version`
   already exists) and the new `ShopOrderOperation` table with its unique
   `(TenantId, Key)` constraint. Give it a clear name such as
   `AddShopOrderOperations`. After generating it, open the migration file
   and confirm it only contains these intended changes.
7. Add a new Minimal API endpoint:
   `PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status`. Require a
   valid JWT and the `Shop.Orders.Manage` permission (added by `B042`, not
   enforced there — this is the task that enforces it). On success return
   `200` with an `AdminOrderDetailResponse` (the same response shape `B042`
   defined).
8. Require every request to this endpoint to send both:
   - `ExpectedVersion` in the request body — the optimistic-concurrency
     token (a number the client last saw for this order's `Version`;
     the server uses it to detect two requests racing to change the same
     order).
   - An `Idempotency-Key` header containing a UUID (idempotent = "safe to
     retry with the same key without repeating the side effect" — a UUID
     is a randomly generated unique identifier string).
9. Implement the allowed status transitions, exactly as follows, and no
   others:
   - `Paid -> Fulfilled` (via action `Fulfill`).
   - `PendingPayment -> Cancelled` (via action `Cancel`).
   - Any other requested transition (including from `Fulfilled` or
     `Cancelled` to anything else — there is no transition out of either of
     those two states) must return `409` with error code
     `invalid_order_transition`.
10. Implement idempotency-key replay handling:
    - If the same `Idempotency-Key` is sent again with the same action,
      look up the stored `ShopOrderOperation` row and return its stored
      response snapshot again — do not redo the state change.
    - If the same `Idempotency-Key` is sent again but with a *different*
      action than what was stored, treat this as a conflict (do not
      silently pick one) and return an error rather than performing either
      action.
11. Implement `TryFulfill(DateTimeOffset nowUtc, int expectedVersion)` on
    the order aggregate: it only succeeds when the order is currently
    `Paid`; it sets status to `Fulfilled`, sets `FulfilledAtUtc = nowUtc`,
    and checks `expectedVersion` matches the order's current `Version`
    before applying the change (increment `Version` on success). Fulfil
    must never touch stock/inventory.
12. Implement `TryCancel(DateTimeOffset nowUtc, int expectedVersion)` on
    the order aggregate: it only succeeds when the order is currently
    `PendingPayment`; it sets status to `Cancelled`, sets
    `CancelledAtUtc = nowUtc`, and checks `expectedVersion` matches the
    order's current `Version` before applying the change (increment
    `Version` on success).
13. Inside the same database transaction as a successful cancel:
    - Lock the order row and the referenced product-variant rows (row lock
      = "a database lock that blocks other transactions from changing the
      same row until this transaction finishes," used here so two
      concurrent cancels cannot both restore stock).
    - Restore each order-item's quantity back to its variant's available
      stock, but only if `InventoryReleasedAtUtc` is still `null` on the
      order (this is the guard that makes the restore happen exactly once
      even if cancel is somehow triggered twice).
    - Set `InventoryReleasedAtUtc = nowUtc` in that same transaction.
    - Invalidate any payment attempts on the order that are still in the
      `Initiated` state (mark them so they can no longer be completed).
    - Do not delete any order history rows.
14. Use `TimeProvider` (the repo's injectable clock, instead of calling
    `DateTime.UtcNow` directly) for `nowUtc` in steps 11–13.
15. Make sure a late payment-gateway callback that arrives for an order
    that is already `Cancelled` can never move it to `Paid`. This rule must
    keep holding after tasks `B044`/`B045` are built — do not implement
    anything in this task that would let a future task bypass it; just
    make sure the current cancel logic and status check enforce it now.
16. Explicitly reject any attempt to cancel or refund an order that is
    already `Paid` and has moved past cancellation — do this by relying on
    the transition table in step 9 (there is no `Paid -> Cancelled` path)
    rather than adding special-case code. A full refund flow does not exist
    yet and is out of scope (see "Non-goals").
17. Apply the "Security and transaction rules" section below: put
    `TenantId` first in every database predicate; never take `TenantId`,
    totals, prices, stock deltas, permission keys, or payment success from
    client input when the server already knows them; use a database
    transaction for the whole fulfil/cancel + inventory-release write (more
    than one persisted invariant changes together); rely on the unique
    `(TenantId, Key)` constraint and row locks from steps 5 and 13 to
    handle races — do not rely only on an earlier existence check
    (`AnyAsync`) with no lock/constraint backing it; thread a
    `CancellationToken` (an object that lets a request be cancelled if the
    client disconnects — pass it through every new async database/I/O
    call) through all new async calls; never log credentials, bearer
    tokens, phone numbers, tracking codes, coupon codes, gateway authority
    values, or customer addresses — only stable IDs and reason codes.
18. Return a generic `404` for a malformed order ID or one belonging to a
    different tenant, after the authorization check runs, matching `B042`'s
    behavior.
19. Write the integration tests listed in "Integration tests required"
    below, one test (or a small group of tests) per bullet. Put them in the
    closest existing `Shop*IntegrationTests.cs` file, or create a new file
    named after this feature if none fits. Use the real PostgreSQL test
    fixture already used by other Shop tests. Assert on response bodies and
    on what was actually persisted — a test that only checks the HTTP
    status code is not sufficient.
20. Update the matching section of `docs/design/shop/http-contracts.md` so
    it exactly matches the route, records, nullability, enums, and problem
    codes you actually delivered.
21. Update `docs/modules/SHOP.md` so it matches the routes/entities/config/
    auth/tests you delivered, and write or update the
    `docs/learning/<task-id>-<slug>.md` learning note.
22. Run every command in "Validation" below, in order, and fix anything
    that fails before asking for approval.
23. Go through "Acceptance checklist" below and confirm each box with real
    evidence (test output, screenshots via `F061`, etc.) before requesting
    final approval.

## Read before editing

Read `AGENTS.md`, the `B043` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/039-shop-order-operations.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

order domain/map/contracts/admin feature, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| PATCH | `/api/tenants/{tenantId}/shop/orders/{orderId}/status` | JWT + `Shop.Orders.Manage` | `200 AdminOrderDetailResponse` |

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = "the repo's standard JSON error body shape for HTTP errors — reuse the existing helper, do not invent a new error format"). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add `FulfilledAtUtc`, `CancelledAtUtc`, `InventoryReleasedAtUtc`, `Version` to `ShopOrder`. Add `OrderStatusAction` values exactly `Fulfill` and `Cancel`.
2. Allowed: Paid -> Fulfilled; PendingPayment -> Cancelled. Same action replay with the same idempotency key returns the stored final representation. All other transitions return `409 invalid_order_transition`. There is no transition out of Fulfilled/Cancelled.
3. Require `ExpectedVersion` plus `Idempotency-Key` UUID. Persist `ShopOrderOperation` (`TenantId`, `OrderId`, key, canonical action, response snapshot, actor, created time) with unique `(TenantId, Key)`.
4. Cancellation locks order and referenced variants, restores each order-item quantity only when `InventoryReleasedAtUtc` is null, marks it in the same transaction, and invalidates any Initiated payment attempts. It does not delete order history. Fulfil does not touch stock.
5. A late payment callback for a Cancelled order must never mark it Paid; B044/B045 preserve this rule. Paid cancellation/refund is explicitly rejected until a refund slice exists.

## Required code shape

```csharp
public sealed record ChangeOrderStatusRequest(string? Action, int ExpectedVersion);

internal bool TryFulfill(DateTimeOffset nowUtc, int expectedVersion) { /* Paid only */ }
internal bool TryCancel(DateTimeOffset nowUtc, int expectedVersion) { /* PendingPayment only */ }
// Release inventory and mark InventoryReleasedAtUtc in one database transaction.
```

Complete these method bodies fully — do not leave the `/* ... */` comments
in production code. Step 11 and step 12 of "Do this in order" above spell
out exactly what each method must check and change. The inventory-release
comment in the third line means: inside the same database transaction as a
successful cancel, restore stock and set `InventoryReleasedAtUtc` together
(see step 13).

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test (or small group of tests) per scenario below. Each must assert on response bodies and persisted data, not status code alone.

- [ ] Write a test that fulfils a `Paid` order, then assert status becomes `Fulfilled`, `FulfilledAtUtc` is set, and stock is untouched.
- [ ] Write a test that cancels a `PendingPayment` order, then assert stock is restored and `InventoryReleasedAtUtc` is set.
- [ ] Write a test that sends a late payment callback for an already-`Cancelled` order, then assert it is rejected and the order never becomes `Paid`.
- [ ] Write a test that replays the same `Idempotency-Key` with the same action twice, then assert the second call returns the same stored response without repeating the side effect.
- [ ] Write a test that reuses the same `Idempotency-Key` with a different action, then assert it is rejected as a conflict.
- [ ] Write a test that sends a stale `ExpectedVersion`, then assert it is rejected and no change is applied.
- [ ] Write a test that fires two concurrent cancel requests for the same order, then assert stock is restored exactly once.
- [ ] Write a test for each of Manage/View/Owner/no-permission, then assert the correct status code and body for each.
- [ ] Write a test that targets a cross-tenant order ID, then assert a generic `404`.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F061` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Paid refunds, return merchandise, carrier integration, partial fulfilment/cancellation or editable customer address.

## Acceptance checklist

- [ ] The visible outcome works through `F061` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
