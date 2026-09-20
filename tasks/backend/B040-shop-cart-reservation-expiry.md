# B040 — Expire abandoned carts and release reserved stock

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S36`.
- Depends on: `B031`.
- Immediate browser consumer: `F058`. Do not widen this API for an unnamed future screen.
- Visible outcome: Stock reserved by a guest cart returns to inventory after a configured lease; active checkout reports an honest expiry instead of leaking stock forever.

## Read before editing

Read `AGENTS.md`, the `B040` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/036-shop-cart-reservation-expiry.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

cart domain/map/contracts/feature, new expiry service and worker, configuration, migration, order creation, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

No new public route. Existing cart mutations/read, checkout summary and order creation gain expiry behavior and additive expiry fields.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add `Status` (`Active`, `Converted`, `Expired`), `LastTouchedAtUtc`, `ExpiresAtUtc`, `ClosedAtUtc?` to `ShopCart`; migrate existing carts as Active with expiry based on migration time plus the configured lease. Add index `(Status, ExpiresAtUtc)`.
        2. Configure `Shop:CartReservationMinutes` (default Development 30, allowed 5..1440) and `Shop:CartCleanupIntervalSeconds` (30..3600). Register `TimeProvider.System`, `ShopCartExpiryService` scoped and `ShopCartCleanupWorker` hosted. Validate values at activation.
        3. Any successful add/update/delete extends the lease. A read does not. Checkout summary and order creation first call `ExpireIfNeededAsync`; an expired cart returns RFC7807 status `410`, `type=shop_cart_expired`.
        4. Expiry transaction atomically locks the cart row, changes Active -> Expired once, groups its items by variant, restores stock quantities, removes cart items, and commits. Concurrent worker/request execution must restore each quantity exactly once.
        5. Successful order creation sets cart Converted and `ClosedAtUtc` in the same transaction that consumes items. Do not delete the cart row. Order creation and expiry serialize on the same cart row.
        6. Add `expiresAtUtc` to `CreateCartResponse` and `CartResponse`. Do not accept client clocks or lease values.


## Required code shape

```csharp
        internal enum ShopCartStatus { Active, Converted, Expired }

        internal interface IShopCartExpiryService
        {
            Task<CartLeaseResult> EnsureActiveAsync(Tsid tenantId, Tsid cartId, CancellationToken ct);
            Task<int> ExpireDueAsync(int batchSize, CancellationToken ct);
        }

        public sealed record CartResponse(
            string CartId, IReadOnlyList<CartItemResponse> Items,
            decimal SubTotal, DateTimeOffset ExpiresAtUtc);
        ```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

lease extension; expiry releases multiple variants; second expiry no-op; worker batch bound; concurrent order vs expiry has one winner and correct stock; converted cart never expires; expired GET/checkout/order 410; config fail closed; tenant isolation.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F058` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Distributed scheduler, durable job platform, signed-in cart merge or changing price snapshots.

## Acceptance checklist

- [ ] The visible outcome works through `F058` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
