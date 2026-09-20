# B041 — Add enforceable coupon limits and atomic redemption

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S37`.
- Depends on: `B040, B029`.
- Immediate browser consumer: `F059`. Do not widen this API for an unnamed future screen.
- Visible outcome: Sellers define minimum subtotal, maximum discount and total redemption limits; checkout previews rules and order creation consumes capacity exactly once.

## Do this in order

Before step 1, follow the "Read before editing" section below: read `AGENTS.md`, the `B041` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, the linked slice file `tasks/slices/037-shop-coupon-rules.md`, and the matching section of `docs/design/shop/http-contracts.md`. Follow the branch-naming, approval-gate and ledger-update rules already described in the "Ownership and dependency" section and in `AGENTS.md` — do not repeat them, just follow them.

1. Add these columns to the existing `ShopCoupon` entity: `MinimumSubtotal` (`decimal`), `MaximumDiscountAmount` (nullable `decimal`), `RedemptionLimit` (nullable `int`), `RedeemedCount` (`int`), `Version` (`int`, for optimistic concurrency).
2. Add these constraints, enforced in validation code (and, where the module already uses database check constraints for similar rules, also as a database constraint):
   - `MinimumSubtotal` and `MaximumDiscountAmount` must both be `>= 0`.
   - `RedemptionLimit`, when set, must be between `1` and `1,000,000` inclusive.
   - `RedeemedCount` must always be `>= 0`, and whenever `RedemptionLimit` is set, `RedeemedCount` must always be `<= RedemptionLimit`.
3. Add an EF migration (run `dotnet ef migrations add <Name>` in the Shop module project) that adds these columns and backfills existing coupon rows with `MinimumSubtotal = 0`, `MaximumDiscountAmount = null`, `RedemptionLimit = null` (meaning unlimited), `RedeemedCount = 0` (or the correct historical count if the module already tracks redemptions some other way — check current code), `Version = 0`. Inspect the generated migration afterward and confirm it only makes these intended changes.
4. Confirm/keep these existing discount-type rules unchanged: percentage discounts must be `1..100`; fixed-amount discounts must be `> 0`.
5. Add the rule that `MaximumDiscountAmount`, when set, caps the discount after the percentage (or fixed amount) is calculated — i.e. compute the raw discount first, then clamp it down to `MaximumDiscountAmount` if the raw discount would exceed it.
6. Add the rule that the final discount amount actually applied must always be `<= subtotal` (never discount more than the order is worth).
7. Create a new static class `ShopCouponPolicy` with a single method `Evaluate(coupon, subtotal, now, consume)` (see exact signature in "Required code shape" below) that centralizes every coupon rule check in one place. Implement it to check the rules in this order, stopping at the first failure, and returning the matching stable reason code:
   a. `coupon_not_found` — the coupon record itself doesn't exist / doesn't belong to this tenant (the caller passes `null`/absent coupon into this check the same way as a not-found case, or checks existence before calling — pick whichever the module's existing pattern already uses, and be consistent).
   b. `coupon_inactive` — the coupon's `IsActive` flag is false.
   c. `coupon_expired` — `coupon.ExpiresAtUtc` is set and `now` is past it.
   d. `coupon_minimum_not_met` — `subtotal < coupon.MinimumSubtotal`.
   e. `coupon_limit_reached` — `coupon.RedemptionLimit` is set and `coupon.RedeemedCount >= coupon.RedemptionLimit`.
   If none of these fail, compute the discount amount using steps 4-6 above and return a successful `CouponEvaluation` with `IsValid: true` and the computed `DiscountAmount`. Never reveal in any public-facing validation message whether a given code belongs to a different tenant — always return the same `coupon_not_found`-style response for "doesn't exist" and "belongs to someone else."
8. In the checkout summary endpoint, call `ShopCouponPolicy.Evaluate` in preview mode only: it must never increment `RedeemedCount` or otherwise persist anything. Checkout summary only shows what would happen.
9. In order creation, inside the same existing database transaction that already creates the order: lock the coupon row, call `ShopCouponPolicy.Evaluate` again (re-validate — do not trust the earlier checkout-summary preview), and if valid, increment `RedeemedCount` by exactly 1 and proceed to create the order using the computed discount. If two order-creation requests race for the last available redemption, the row lock must ensure only one succeeds with the discount applied; the other must fail its re-validation (typically with `coupon_limit_reached`) and that customer's cart must remain active/untouched by this failure (they can retry without the coupon or with a different one).
10. Add `PUT /api/tenants/{tenantId}/shop/coupons/{couponId}`, protected by the `Shop.Shipping.Manage` permission (this is the existing permission key already used for the coupon POST endpoint and other shipping/coupon management routes — reuse it, do not invent a new permission for this task). Request body is `UpdateCouponRequest` (see "Required code shape"). Implement it with these exact rules:
    a. Requires `ExpectedVersion` matching the coupon's current `Version`; otherwise return `409 Conflict` (stale update), matching the optimistic-concurrency pattern used elsewhere in Shop.
    b. Reject (validation error) any attempt to set `RedemptionLimit` below the coupon's current `RedeemedCount`.
    c. Reject (validation error) any attempt to change the coupon's normalized code or its discount type, once the coupon has been redeemed at least once (`RedeemedCount > 0`). Other fields (limits, expiry, `IsActive`) may still be changed after redemptions have happened.
    d. Setting `IsActive` to `false` (deactivating) must always be allowed, even after redemptions, subject only to the `ExpectedVersion` check.
11. Extend the existing coupon POST (create) endpoint's request/response to carry the new fields (`MinimumSubtotal`, `MaximumDiscountAmount`, `RedemptionLimit`) per `CreateCouponRequest` in "Required code shape", validating them with the rules from steps 2 and 4.
12. Add the integration tests listed in "Integration tests required" below, in the closest existing `Shop*IntegrationTests.cs` file (or a new file named after this feature if none fits). Use the real PostgreSQL test fixture. Every test must assert on the actual response body and/or actual database row — a status-code-only test is not sufficient.
13. Update `docs/modules/SHOP.md` (create it if it does not exist yet) so its routes/entities/config/auth/tests sections match exactly what you built.
14. Update the matching section of `docs/design/shop/http-contracts.md` to the delivered wire contract before this Spec file is deleted at task completion.
15. Write the `docs/learning/B041-<slug>.md` learning note per the "Backend learning note" rules in `AGENTS.md`.
16. Run every command listed under "Validation" below, in order, before asking for final approval.

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

Steps 7-11 in "Do this in order" above already spell out the exact rule order and logic for `ShopCouponPolicy.Evaluate` and both endpoints — treat that as the completed implementation of this shape, not a placeholder. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

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
    internal static CouponEvaluation Evaluate(ShopCoupon coupon, decimal subtotal, DateTimeOffset nowUtc) { /* exact ordered rules — see "Do this in order" step 7 */ }
}
```

Note: the method signature mentioned in prose (`Evaluate(coupon, subtotal, now, consume)`) and the code block's signature (`Evaluate(coupon, subtotal, nowUtc)`) both describe the same method; `consume` in the prose refers to whether the caller is in "preview" mode (checkout summary, step 8) or "consume" mode (order creation, step 9) — implement this either as a boolean parameter on `Evaluate` itself, or as `Evaluate` always being read-only and a separate small method that increments `RedeemedCount` after a successful `Evaluate` inside the order-creation transaction. Pick whichever fits the module's existing style; if unsure, keep `Evaluate` pure (no side effects) and increment `RedeemedCount` in the order-creation code right after a successful evaluation, inside the same transaction and row lock.

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync` (an `AnyAsync` "does a row already exist" check followed by a separate write is not safe under concurrency by itself — the row lock on the coupon in order creation is what actually prevents double redemption).
- Thread `CancellationToken` (the standard .NET signal that lets a caller cancel an in-flight async operation) through new I/O.
- Use `TimeProvider` (the repo's injectable clock abstraction, instead of calling `DateTimeOffset.UtcNow` directly, so tests can control time) where this task adds time-dependent behavior (e.g. `coupon_expired` checks).
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each test must assert on the real response body and/or the real database row, not just the HTTP status code.

- [ ] Write one test per `ShopCouponPolicy.Evaluate` rule boundary: a coupon that doesn't exist/belongs to another tenant (`coupon_not_found`), an inactive coupon (`coupon_inactive`), an expired coupon (`coupon_expired`), a subtotal below `MinimumSubtotal` (`coupon_minimum_not_met`), and a coupon at its `RedemptionLimit` (`coupon_limit_reached`). Assert each returns its exact reason code.
- [ ] Write a test with a percentage coupon whose raw computed discount would exceed `MaximumDiscountAmount`, then assert the applied discount is capped at `MaximumDiscountAmount`.
- [ ] Write a test that calls checkout summary with a valid coupon, then reloads the coupon from the database, then asserts `RedeemedCount` did not change (preview never redeems).
- [ ] Write a test that completes an order with a valid coupon, then asserts `RedeemedCount` incremented by exactly 1 and the order's stored discount matches the evaluated amount.
- [ ] Write a test where order creation fails after coupon evaluation succeeds (e.g. a downstream failure that rolls back the transaction), then asserts `RedeemedCount` was not incremented (the rollback undid it).
- [ ] Write a test that races two order-creation requests against the same coupon when only one redemption slot remains, then asserts exactly one succeeds with the discount applied and the other fails validation, and `RedeemedCount` only increased by 1 total.
- [ ] Write a test that submits a PUT update with a stale `ExpectedVersion`, then asserts `409 Conflict`.
- [ ] Write a test that submits a PUT update trying to set `RedemptionLimit` below the coupon's current `RedeemedCount`, then asserts a validation error and no change persisted.
- [ ] Write a test that creates a coupon for tenant A, then asserts tenant B cannot read, update or redeem it, and that any not-found-style response looks identical to a truly nonexistent code.
- [ ] Write a test that runs the migration against pre-existing coupon rows, then asserts they end up with `RedemptionLimit: null` (unlimited) and are otherwise usable exactly as before.

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
