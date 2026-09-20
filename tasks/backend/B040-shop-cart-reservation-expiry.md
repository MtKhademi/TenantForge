# B040 — Expire abandoned carts and release reserved stock

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S36`.
- Depends on: `B031`.
- Immediate browser consumer: `F058`. Do not widen this API for an unnamed future screen.
- Visible outcome: Stock reserved by a guest cart returns to inventory after a configured lease; active checkout reports an honest expiry instead of leaking stock forever.

## Do this in order

Before step 1, follow the "Read before editing" section below: read `AGENTS.md`, the `B040` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, the linked slice file `tasks/slices/036-shop-cart-reservation-expiry.md`, and the matching section of `docs/design/shop/http-contracts.md`. Follow the branch-naming, approval-gate and ledger-update rules already described in the "Ownership and dependency" section and in `AGENTS.md` — do not repeat them, just follow them.

1. Add these columns to the existing `ShopCart` entity: `Status` (an enum with exactly three values: `Active`, `Converted`, `Expired`), `LastTouchedAtUtc` (`DateTimeOffset`), `ExpiresAtUtc` (`DateTimeOffset`), `ClosedAtUtc` (nullable `DateTimeOffset`).
2. Add an EF migration (run `dotnet ef migrations add <Name>` in the Shop module project) that: adds those columns, backfills every existing cart row's `Status` to `Active`, and sets `ExpiresAtUtc` for existing rows to the migration's run time plus the configured lease minutes (see step 3). Add a composite index on `(Status, ExpiresAtUtc)` in the same migration. Inspect the generated migration afterward and confirm it only makes these intended changes.
3. Add two configuration values read from the module's configuration section, named exactly `Shop:CartReservationMinutes` and `Shop:CartCleanupIntervalSeconds`:
   - `Shop:CartReservationMinutes` — default `30` in Development; must be validated at startup ("activation") to be within `5..1440` inclusive, or the app must fail to start ("fail closed").
   - `Shop:CartCleanupIntervalSeconds` — must be validated at startup to be within `30..3600` inclusive, or the app must fail to start.
4. Register these with the dependency injection container: `TimeProvider.System` (the standard .NET injectable clock, registered as a singleton `TimeProvider`, so cart expiry logic never calls `DateTimeOffset.UtcNow` directly and tests can control time), `ShopCartExpiryService` as scoped, and `ShopCartCleanupWorker` as a hosted background service (a hosted service is a long-running background task the .NET host starts and stops automatically).
5. Implement `IShopCartExpiryService` exactly as shown in "Required code shape" below:
   - `EnsureActiveAsync(tenantId, cartId, ct)`: loads the cart row for `(tenantId, cartId)`, locks it (row lock or equivalent transaction isolation), and checks whether `now >= ExpiresAtUtc` and `Status == Active`. If so, this is exactly the expiry transaction described in step 7 below — run it, then return a `CartLeaseResult` indicating the cart is now expired. If the cart is still `Active` and not due, return a `CartLeaseResult` indicating it is still active (do not change anything). If the cart's `Status` is already `Expired` or `Converted`, return the corresponding result without re-running expiry.
   - `ExpireDueAsync(batchSize, ct)`: selects up to `batchSize` `Active` carts whose `ExpiresAtUtc <= now`, ordered so the batch is deterministic, and runs the expiry transaction (step 7) on each one it can lock. Returns the count of carts it actually expired. This is what the cleanup worker calls repeatedly.
6. Any successful cart mutation (add item, update item quantity, delete item) must extend the lease: set `LastTouchedAtUtc = now` and `ExpiresAtUtc = now + CartReservationMinutes`. A plain read of the cart (GET) must never extend the lease or change these fields.
7. Implement the "expiry transaction" (used by both `EnsureActiveAsync` and `ExpireDueAsync`) exactly like this, all inside one database transaction:
   a. Lock the cart row (so a concurrent worker run and a concurrent request can't both act on the same cart at once).
   b. If `Status` is not `Active`, stop here and do nothing further (this guarantees each cart's stock is only ever restored once, even under concurrent execution).
   c. Change `Status` from `Active` to `Expired` and set `ClosedAtUtc = now`.
   d. Group the cart's line items by product variant, and for each variant, add its reserved quantity back to that variant's available stock.
   e. Remove the cart's line item rows.
   f. Commit the transaction. If anything above fails, the whole transaction rolls back and no stock is restored twice.
8. In the checkout summary endpoint and in order creation, before doing anything else with the cart, call `EnsureActiveAsync` (step 5) — if the cart turns out to be (or already was) `Expired`, stop and return an RFC7807 (the repo's standard JSON error body shape — reuse the existing helper) error with HTTP status `410 Gone` and `type=shop_cart_expired`. Do not proceed with checkout/order logic for an expired cart.
9. When order creation succeeds, inside the same database transaction that consumes the cart's items, set the cart's `Status = Converted` and `ClosedAtUtc = now`. Do not delete the cart row — it stays in the database as a historical record. Because order creation and the expiry transaction both lock the same cart row, only one of them can win if they race (see the "concurrent order vs expiry" test below).
10. Add `ExpiresAtUtc` (as `expiresAtUtc` in JSON) to both `CreateCartResponse` and `CartResponse` (see exact shape in "Required code shape"). The server always computes and returns this value — never accept a client-supplied clock value or a client-supplied lease length from the request body.
11. Add the integration tests listed in "Integration tests required" below, in the closest existing `Shop*IntegrationTests.cs` file (or a new file named after this feature if none fits). Use the real PostgreSQL test fixture. Every test must assert on the actual response body and/or actual database state — a status-code-only test is not sufficient.
12. Update `docs/modules/SHOP.md` (create it if it does not exist yet) so its routes/entities/config/auth/tests sections match exactly what you built.
13. Update the matching section of `docs/design/shop/http-contracts.md` to the delivered wire contract (the additive `expiresAtUtc` field, the new `410 shop_cart_expired` error) before this Spec file is deleted at task completion.
14. Write the `docs/learning/B040-<slug>.md` learning note per the "Backend learning note" rules in `AGENTS.md`.
15. Run every command listed under "Validation" below, in order, before asking for final approval.

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

Step 5 and step 7 in "Do this in order" above already spell out the exact logic each method and each response field must implement — treat that as the completed implementation of this shape, not a placeholder. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

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

`Tsid` here is the TSID type (a sortable numeric string ID — see `TenantForge.BuildingBlocks`) already used for IDs elsewhere in the module. `CartLeaseResult` is a small result type you define to carry whether the cart is active/expired/converted back to the caller — name and shape it consistently with how the module already returns similar outcome types from other services; if unsure, return an enum-like result plus the current `ShopCart` state.

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync` (an `AnyAsync` "does a row already exist" check followed by a separate write is not safe under concurrency by itself — the row lock is what actually prevents the race here).
- Thread `CancellationToken` (the standard .NET signal that lets a caller cancel an in-flight async operation) through new I/O.
- Use `TimeProvider` (the repo's injectable clock abstraction, instead of calling `DateTimeOffset.UtcNow` directly, so tests can control time) where this task adds time-dependent behavior — this task adds a lot of it, since expiry is entirely time-driven.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each test must assert on the real response body and/or the real database row, not just the HTTP status code.

- [ ] Write a test that mutates a cart (add/update/delete an item), then asserts `LastTouchedAtUtc` and `ExpiresAtUtc` both moved forward.
- [ ] Write a test with a cart containing items from multiple product variants, let it expire, then assert every variant's stock was restored by the correct quantity.
- [ ] Write a test that expires a cart once, then runs expiry on it again, then asserts the second run is a no-op (no double stock restoration, `Status` stays `Expired`).
- [ ] Write a test with more due carts than `batchSize`, then assert `ExpireDueAsync` only processes up to `batchSize` carts in one call.
- [ ] Write a test that races order creation against expiry on the same cart (e.g. by controlling `TimeProvider` and triggering both concurrently), then assert exactly one of them wins and the resulting stock count is correct either way.
- [ ] Write a test that converts a cart via order creation, then advances time past its old `ExpiresAtUtc`, then asserts the converted cart is never picked up by `ExpireDueAsync` and its `Status` stays `Converted`.
- [ ] Write a test that expires a cart, then calls cart GET, checkout summary and order creation against it, then asserts all three return `410` with RFC7807 `type=shop_cart_expired`.
- [ ] Write a test that sets `Shop:CartReservationMinutes` or `Shop:CartCleanupIntervalSeconds` outside their allowed ranges, then asserts the application fails to start (fails closed) rather than starting with an invalid value.
- [ ] Write a test that creates carts for two different tenants, then asserts tenant A's expiry/cleanup never touches or restores stock belonging to tenant B.

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
