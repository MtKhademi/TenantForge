# B041 — Add enforceable coupon limits and atomic redemption

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S37`.
- Depends on: `B040, B029`.
- Immediate browser consumer: `F059`. Do not widen this API for an unnamed future screen.
- Visible outcome: Sellers define minimum subtotal, maximum discount and total redemption limits; checkout previews rules and order creation consumes capacity exactly once.

## Read before editing

Read `AGENTS.md`, the `B041` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/037-shop-coupon-rules.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

coupon/order domain + maps/contracts/features, migration, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

Extend POST coupon. Add `PUT /api/tenants/{tenantId}/shop/coupons/{couponId}` protected by `Shop.Shipping.Manage`; preserve deactivate. Checkout/order routes consume the new rules.

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add `MinimumSubtotal decimal`, `MaximumDiscountAmount decimal?`, `RedemptionLimit int?`, `RedeemedCount int`, `Version int` to `ShopCoupon`. Constraints: money >=0, limits 1..1,000,000, count >=0 and <= limit when set.
        2. Percentage stays 1..100; fixed discount >0. Maximum discount applies after percentage calculation. Final discount is always <= subtotal.
        3. Centralize evaluation in `ShopCouponPolicy.Evaluate(coupon, subtotal, now, consume)` returning stable reason codes: `coupon_not_found`, `coupon_inactive`, `coupon_expired`, `coupon_minimum_not_met`, `coupon_limit_reached`. Public validation messages do not reveal whether another tenant owns a code.
        4. Checkout summary only previews and never increments. Order creation runs inside its existing transaction, locks the coupon row, re-evaluates, increments `RedeemedCount` exactly once, and creates the order. Concurrent final redemption yields one order using the discount; the loser receives validation and retains an active cart.
        5. Admin update requires `ExpectedVersion`, cannot reduce limit below redeemed count, and cannot edit normalized code or discount type after first redemption; it may deactivate safely.


## Required code shape

```csharp
        public sealed record CreateCouponRequest(
            string? Code, string? DiscountType, decimal DiscountValue,
            decimal MinimumSubtotal, decimal? MaximumDiscountAmount,
            int? RedemptionLimit, DateTimeOffset? ExpiresAtUtc);
        public sealed record UpdateCouponRequest(
            decimal DiscountValue, decimal MinimumSubtotal, decimal? MaximumDiscountAmount,
            int? RedemptionLimit, DateTimeOffset? ExpiresAtUtc, bool IsActive, int ExpectedVersion);

        public sealed record CouponResponse(
            string Id, string Code, string DiscountType, decimal DiscountValue,
            decimal MinimumSubtotal, decimal? MaximumDiscountAmount,
            int? RedemptionLimit, int RedeemedCount, bool IsActive,
            DateTimeOffset? ExpiresAtUtc, int Version);

        internal sealed record CouponEvaluation(bool IsValid, decimal DiscountAmount, string? ErrorCode);
        internal static class ShopCouponPolicy
        {
            internal static CouponEvaluation Evaluate(ShopCoupon coupon, decimal subtotal, DateTimeOffset nowUtc) { /* exact ordered rules */ }
        }
        ```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

all rule boundaries; maximum cap; preview does not redeem; order redeems once; failed order rollback does not redeem; last-use race; stale update; limit cannot fall below count; tenant isolation; existing unlimited coupons migrate with null limits.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F059` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Per-customer limits, product/category-specific coupons, stacking, campaigns or automatic promotions.

## Acceptance checklist

- [ ] The visible outcome works through `F059` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
