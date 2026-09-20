# B043 — Fulfil or cancel orders with inventory-safe transitions

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S39`.
- Depends on: `B042, B040`.
- Immediate browser consumer: `F061`. Do not widen this API for an unnamed future screen.
- Visible outcome: An operator marks paid orders fulfilled or cancels unpaid orders; cancellation restores reserved stock exactly once.

## Read before editing

Read `AGENTS.md`, the `B043` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/039-shop-order-operations.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

order domain/map/contracts/admin feature, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| PATCH | `/api/tenants/{tenantId}/shop/orders/{orderId}/status` | JWT + `Shop.Orders.Manage` | `200 AdminOrderDetailResponse` |

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

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

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Paid fulfil; pending cancel stock restore; cancelled late callback rejected; duplicate key replay; changed action same key conflict; stale version; two concurrent cancels restore once; permission matrix; cross-tenant order 404.

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
