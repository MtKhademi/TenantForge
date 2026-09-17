# B029 — Shipping-rate and coupon admin API

One backend slice: two new tenant-scoped admin surfaces in the Shop module —
per-province shipping rates (upsert, no delete) and coupon codes
(create/list/deactivate) — both authorized by the exact same
tenant-membership pattern B026 established. No new permission concept, no
new module, no public (anonymous) endpoint: the anonymous checkout summary
that *consumes* these rows is B030's job.

## 1. Files changed and why

| File | Why |
| --- | --- |
| `domain/ShopShippingRate.cs` | One row per Iranian province the tenant actually ships to. A missing row means "we don't ship there" — B030 must say that plainly instead of charging zero. `UpdateCost` is the only mutation: correction is by overwriting, never by delete (Spec non-goal). |
| `domain/ShopCoupon.cs` (+`ShopDiscountType` enum) | `Code` (as submitted, trimmed) plus `NormalizedCode` (upper) — the duplicate check and unique index run on the normalized form so `welcome10` and `WELCOME10` are one code. Carries only what B030 actually validates: `IsActive`, `ExpiresAtUtc`. No usage counts, no minimum-order-value — deliberate non-goals. |
| `infrastructure/ShopShippingRateMap.cs` / `ShopCouponMap.cs` | `shop_shipping_rates` / `shop_coupons`; TSID→`bigint` via `ShopTsidValueConverter`; `numeric(12,2)` money; unique `(tenant_id, province_name)` and `(tenant_id, normalized_code)` indexes — the database backstop for the in-handler duplicate checks. `discount_type` is persisted as a string (`HasConversion<string>()`), readable in the DB without enum-code archaeology. |
| `infrastructure/ShopDbContext.cs` | +2 `DbSet`s, +2 maps — the only way entities enter the model. |
| `infrastructure/Migrations/20260917173716_AddShopShippingRatesAndCoupons*.cs` | **Generated** migration: the two tables and indexes. Review as generated output. |
| `features/shipping/ShippingRateContracts.cs` + `ShippingRatesFeature.cs` | GET list (ordered by province) + POST upsert (200 with a stable id). |
| `features/coupons/CouponContracts.cs` + `CouponsFeature.cs` | POST create (201 / 400 / 409), GET paginated list, PATCH deactivate (200 / 404). |
| `ShopModule.cs` | +2 `Map*Feature()` calls and usings inside the existing seam. |
| `tests/.../IamDbFixture.cs` | `ShopShippingCouponDbFixture`/`...IsolatedCollection` on a dedicated DB (`tenantforge_shop_shipping_coupon_tests`) — the every-test-class-owns-a-database convention. |
| `tests/.../ShopShippingCouponAdminIntegrationTests.cs` | 9 facts: upsert same-id, ordered list, blank-province 400, both discount types + pagination, case-insensitive duplicate 409, same code in another tenant allowed, all 400 field cases, deactivate + 404s, and 401/403 on every route. |
| `tests/.../ShopModuleIntegrationTests.cs` | Same expected, non-weakening edit as B028 had: `ExpectedShopTables` now also lists the two B029 tables (the array documents "the exact Shop tables on the branch this test runs from"). |

## 2. Request flow (coupon create — the most branching one)

`POST /api/tenants/{tenantId}/shop/coupons`

1. `ShopAuthorization.AuthorizeTenantAccessAsync` — parses the route
   tenantId, reads the account from the JWT `sub`, then counts
   active/active/active membership rows in IAM's own
   `iam_tenant_memberships` table with **raw SQL** (Shop cannot EF-reference
   IAM's `internal` entities; the two contexts share one physical database).
   Unauthenticated → `401`, non-member → `403`, before any business logic.
2. Field validation in one pass, per-field 400 errors: blank `code`;
   `Enum.TryParse<ShopDiscountType>` (case-insensitive) for the type;
   `Percentage` constrained to 1–100, `FixedAmount` to > 0.
3. Duplicate check: tenant-scoped `AnyAsync` on `NormalizedCode` →
   RFC-9110 Problem `409 "Duplicate coupon code"` (backed at the DB level by
   the unique index, so even a race between two requests cannot create two
   rows).
4. `ShopCoupon.Create` (normalizes the code, converts expiry to UTC) →
   `SaveChangesAsync` → `201` with the `CouponResponse`.

The shipping-rate POST is the same shape minus validation depth: blank
province → 400, else a tenant-scoped `SingleOrDefaultAsync` on
`(TenantId, ProvinceName)` — found: `UpdateCost` + save; not found:
`ShopShippingRate.Create` + save — either way a `200` with the row.

## 3. Backend concepts introduced

- **Upsert vs create semantics.** The rate POST answers `200` (not `201`)
  and returns a **stable id** on both first create and later corrections —
  the client can trust "one row per province" and never has to track which
  call created the row. The coupon POST is create-only: `201` once, then
  `409`. The same verb (`POST`) carrying different lifecycle semantics,
  chosen because the Spec names the rate POST a *set*.
- **Normalized uniqueness.** The domain stores both the display form and the
  normalized form; queries and the unique index use the normalized one.
  This is the standard answer to "codes are unique case-insensitively"
  without provider-dependent collation tricks.
- **Enum-as-string persistence.** `HasConversion<string>()` keeps the DB
  human-readable (`'Percentage'`, not `0`) at the cost of no DB-level
  check that only known values exist — acceptable because the only writer
  validates with `Enum.TryParse` first.
- **Authorization reuse.** Every route is `.RequireAuthorization()` + the
  shared membership check; the 401/403 behavior is asserted on **all four**
  routes in one fact, so a future route that forgets either layer is caught.

## 4. Security decisions

- **Default-deny, server-side.** No route is reachable without a valid JWT;
  no membership → 403, even for a fully authenticated user. The platform
  admin's own JWT is *not* special-cased: access is strictly "am I an active
  member of the tenant in the URL".
- **No cross-tenant leakage.** Every query is scoped by `access.TenantId`
  (the value from the authorized check, not the raw route string); the
  integration fact proves the same coupon code is legal in two tenants and
  each tenant's list shows only its own.
- **404 non-disclosure on deactivate.** Unknown and malformed coupon ids
  both answer `404`; the response never distinguishes "no such coupon" from
  "not your tenant".
- No secrets or tokens in logs; validation text never echoes user input
  beyond the field name.

## 5. Alternatives deliberately postponed

- **`DELETE /shipping-rates/{id}`.** Spec non-goal: an unwanted province is
  corrected by setting a new cost (or left at zero). A delete endpoint
  would need its own "is this referenced?" story and isn't needed by any
  current or immediately dependent front task.
- **Coupon rule engine** (usage caps, minimum order value, per-customer
  limits). The slice explicitly says B030 only validates `IsActive` +
  `ExpiresAtUtc`; building more would be speculative.
- **EF `upsert` (PostgreSQL `ON CONFLICT`).** The read-then-write upsert is
  sufficient here: the unique `(tenant_id, province_name)` index makes a
  true race fail loudly (a 500) rather than silently duplicate — and this
  is a low-frequency admin screen, not a hot path. A concurrent-write
  retry would be infrastructure no visible slice needs.
- **A `docs/modules/Shop.md` handbook / `Shop.Contract` project.** Same
  admission rule as B027/B028: not yet earned; these contracts stay
  module-local.

## 6. Verify it

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
# focused:
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~ShopShippingCouponAdminIntegrationTests"
```

Manual (Development, member JWT, exactly what the demo did):

```bash
curl -X POST .../api/tenants/<tenantId>/shop/shipping-rates \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"provinceName":"Tehran","cost":50000}'                      # 200, new id
# same call with "cost":75000  ->  200, SAME id, new cost
curl -X POST .../api/tenants/<tenantId>/shop/coupons \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"code":"WELCOME10","discountType":"Percentage","discountValue":10,"expiresAtUtc":null}'   # 201
# repeat (any case)        -> 409 {"title":"Duplicate coupon code"}
curl -X PATCH .../api/tenants/<tenantId>/shop/coupons/<couponId>/deactivate -H "Authorization: Bearer <token>"   # 200, isActive:false
```

## 7. Three review questions

1. The upsert reads the existing row and then writes. Two admins save the
   same province at the same instant: what exactly happens at the database
   level, and why is the failure mode (a 500) acceptable here while it
   would not be on a shopper-facing hot path?
2. `NormalizedCode` duplicates information already present in `Code`. Where
   would this design break if a tenant legitimately wanted two coupons that
   differ only by case — and which of "normalized uniqueness" vs
   "case-sensitive uniqueness" does the Spec actually choose, and how do
   you know from the text?
3. `ShopAuthorization` runs a raw SQL query against IAM's tables instead of
   an EF relationship. Why is that the only option here (assembly
   visibility, module boundaries), and what invariant must keep being true
   for the query to stay correct? (Look at B026's Context note and the
   shared-physical-database decision in `ShopModuleIntegrationTests`.)
