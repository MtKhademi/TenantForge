# B041 — Add enforceable coupon limits and atomic redemption

Slice S37. Sellers can now set a minimum subtotal, a maximum discount cap and a
total redemption limit; checkout previews the rules and order creation consumes
capacity exactly once.

## 1. Files changed and why

- `domain/ShopCoupon.cs` — added `MinimumSubtotal`, `MaximumDiscountAmount`,
  `RedemptionLimit`, `RedeemedCount`, `Version`; extended `Create` with the new
  fields; added `Update` (admin edit, bumps `Version`) and `RecordRedemption`
  (single increment); `Deactivate` now also bumps `Version`.
- `infrastructure/ShopCouponMap.cs` — mapped the five new columns.
- `infrastructure/Migrations/20260922152409_AddShopCouponRules.{cs,Designer.cs}`
  (generated) + `ShopDbContextModelSnapshot.cs` — adds the five columns with
  defaults that backfill existing rows to the Spec's values (unlimited, zero
  redeemed, version zero).
- `features/coupons/ShopCouponPolicy.cs` (new) — the single pure owner of every
  coupon rule plus the stable reason-code messages.
- `features/coupons/CouponContracts.cs` — `CreateCouponRequest` gained three
  fields; new `UpdateCouponRequest`; `CouponResponse` gained five.
- `features/coupons/CouponsFeature.cs` — extended POST validation, new PUT
  endpoint, updated `ToResponse`.
- `features/checkout/CheckoutFeature.cs` — replaced the inline discount math
  with a tenant-first lookup + `ShopCouponPolicy.Evaluate` (preview, no write).
- `features/orders/OrderCreationFeature.cs` — loads the coupon row **tracked and
  `FOR UPDATE`** inside the order transaction, re-evaluates, and increments
  `RedeemedCount` exactly once.
- `Properties/AssemblyInfo.cs` (new) — `InternalsVisibleTo` for the test
  assembly (same as IAM) so the migration test can construct `ShopDbContext`.
- Tests: `ShopCouponRulesIntegrationTests.cs`, `ShopCouponRulesMigrationTests.cs`,
  a new `ShopCouponRulesDbFixture`/collection in `IamDbFixture.cs`.

## 2. Request flow, endpoint to response

**Checkout summary (preview).** Parse ids → verify the cart lease → sum the cart
items for the subtotal → if a `couponCode` was sent, look it up with a
tenant-first predicate (null → `coupon_not_found`, non-leaking) →
`ShopCouponPolicy.Evaluate(coupon, subtotal, now)` → on a valid result render
`discountAmount`; on failure return a `400` naming `couponCode` with the exact
stable code. Nothing is persisted.

**Order creation (consume).** Inside the existing transaction, after the cart
`FOR UPDATE` lock: if a `couponCode` was sent, load the coupon row **tracked and
`FOR UPDATE`** (same tenant-first scoping), run `Evaluate` again (never trust
the preview), and on success call `coupon.RecordRedemption()` and use the
computed discount to build the order. A single `SaveChanges`/commit persists the
order, the cart conversion and the coupon increment atomically.

**Admin update (PUT).** Authorize `Shop.Shipping.Manage` → validate field ranges
(`discountValue` > 0, `minimumSubtotal` ≥ 0, `maximumDiscountAmount` ≥ 0,
`redemptionLimit` null or 1..1,000,000) → load the row (tenant-first) → reject a
`redemptionLimit` below the current `RedeemedCount` (`400`) → reject a stale
`ExpectedVersion` (`409 stale_version`) → `coupon.Update(...)` (bumps `Version`)
→ save → `200` with the full `CouponResponse`.

## 3. Backend concepts introduced

- **Centralizing a rule in one pure function.** `ShopCouponPolicy.Evaluate` has
  no I/O and no side effect; the "preview vs consume" difference lives entirely
  in the two callers. One definition of "is this coupon valid for this subtotal
  at this moment" means the preview and the real charge can never disagree.
- **Row lock held across the write to close a check-then-write race.** The
  `SELECT ... FOR UPDATE` on the coupon row is what serializes two concurrent
  checkouts racing for the last redemption slot; the loser re-reads the bumped
  count and fails `coupon_limit_reached`. A bare `AnyAsync`/read-then-write
  would let both increment (double redemption). The lock order (cart → coupon)
  is the same in every order-creation path, so no deadlock.
- **Optimistic concurrency with a client-managed version counter** — the same
  pattern as `ShopProfile`/product galleries: the caller echoes the version it
  read, a mismatch is a `409`, and every successful save (update *and*
  deactivate) advances it exactly once.
- **Transaction-scoped, non-leaking tenant scoping.** The anonymous routes
  never leak whether a coupon code "does not exist" vs "belongs to another
  tenant": both are the tenant-first lookup returning null → the same
  `coupon_not_found`.

## 4. Important security decisions

- **Server-side re-validation at consume time.** The order endpoint re-runs the
  full rule set under the row lock instead of trusting the earlier checkout
  summary, so a coupon deactivated or exhausted between preview and order
  cannot be applied.
- **`RedeemedCount` is only ever incremented inside order creation's
  transaction, under the coupon row lock** — there is no other write path, so a
  downstream failure that rolls the transaction back also undoes the redemption.
- **Non-leaking not-found** on the anonymous routes (identical message for
  missing vs cross-tenant), and cross-tenant admin read/update are `404`
  (never `403`), consistent with the rest of Shop.
- **The applied discount can never exceed the subtotal** — clamped after the
  percentage/fixed calculation and after the maximum cap.
- No coupon code is ever logged; only stable ids and reason codes would be.

## 5. Alternatives deliberately postponed

- **Database CHECK constraints** for the field ranges — the Shop module uses
  none today, so per the Spec these are enforced in validation code only.
- **A DB trigger / unique-constraint guard** on `RedeemedCount <=
  RedemptionLimit` — the row lock + guarded increment already make this
  unreachable; a constraint would be defensive duplication.
- **Per-customer or product-specific coupons, stacking, campaigns, automatic
  promotions** — explicit non-goals of S37.
- **A `Shop.Contract` project** — no second .NET consumer exists to prove the
  boundary, so the records stay beside the feature.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter "FullyQualifiedName~ShopCouponRulesIntegrationTests|FullyQualifiedName~ShopCouponRulesMigrationTests"
dotnet.exe test TenantForge.sln --nologo
```

Manual (Development): start Docker, run the API, sign in as a tenant owner and
create a coupon with `minimumSubtotal`, `maximumDiscountAmount` and a small
`redemptionLimit`; build a cart whose subtotal clears the minimum; call
`checkout/summary` with the code (see the capped discount, `redeemedCount`
stays 0); create two carts and race two `POST …/orders` for the last slot —
exactly one applies the discount, the other is a `400 coupon_limit_reached` and
its cart is still usable; then `PUT` the coupon with a stale `expectedVersion`
to see the `409`.

## 7. Three review questions for the learner

1. Why must order creation load the coupon row **tracked and `FOR UPDATE`**
   rather than `AsNoTracking`? What would go wrong with a read-then-write if
   two requests raced for the last redemption slot?
2. `ShopCouponPolicy.Evaluate` is pure and has no `consume` parameter — where
   does the "preview vs consume" difference actually live, and why is that
   safer than a boolean flag on the policy?
3. The first three reason codes keep the historical "not valid" wording. What
   would break if we changed that text, and how do the tests pin that decision
   down?
