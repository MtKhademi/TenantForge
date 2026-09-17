---
id: B029
slice: S28
title: Shipping-rate and coupon admin API
agent: backend-mentor
source: tasks/slices/028-shop-checkout.md
---

# Objective

Give an authenticated tenant admin endpoints to set/list per-province
shipping cost rows and to create/list/deactivate coupons, under the same
`/api/tenants/{tenantId}/shop/...` admin prefix and tenant-membership
authorization B026 already established.

# Context

Read `tasks/slices/028-shop-checkout.md` completely. Read
`tasks/backend/B026-category-and-product-admin-api.md`'s already-delivered
code (`features/categories/CategoriesFeature.cs`,
`features/products/ProductsFeature.cs`) for the exact admin-endpoint
authorization/validation/route-mapping style to reuse unchanged — this
task introduces no new authorization concept.

# Scope

1. `domain/ShopShippingRate.cs`, `ShopCoupon.cs` and their
   `IEntityTypeConfiguration` maps, added to `ShopDbContext`. One EF Core
   migration adding exactly these two tables.
2. `features/shipping/ShippingRatesFeature.cs`:
   - `GET /api/tenants/{tenantId}/shop/shipping-rates` — every configured
     province/cost row for the tenant.
   - `POST /api/tenants/{tenantId}/shop/shipping-rates` — body:
     `ProvinceName`, `Cost`. Creates or updates (upsert on
     `ProvinceName` within the tenant — setting a rate for a province
     that already has one replaces its cost rather than creating a
     duplicate row) a rate.
3. `features/coupons/CouponsFeature.cs`:
   - `POST /api/tenants/{tenantId}/shop/coupons` — body: `Code`,
     `DiscountType` (`Percentage`/`FixedAmount`), `DiscountValue`,
     `ExpiresAtUtc` (nullable). Validates `Code` is unique within the
     tenant (case-insensitive) and `DiscountValue` is positive (and, for
     `Percentage`, at most 100).
   - `GET /api/tenants/{tenantId}/shop/coupons` — paginated list of every
     coupon for the tenant (active and inactive).
   - `PATCH /api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate` —
     sets `IsActive = false`. There is no reactivate/edit endpoint in this
     task — deactivation is the one lifecycle transition a boutique owner
     needs; creating a fresh coupon for a new promotion is simpler than
     reactivating a stale one, so that path is not built.

# Non-goals

- No coupon usage tracking/redemption count.
- No shipping-rate delete endpoint — an unwanted province rate is
  corrected by setting a new cost via the same upsert `POST`, or (if truly
  no longer shippable) is a case the team can revisit once a real need for
  deletion is demonstrated; this task does not speculatively add one.

# Acceptance

- All four endpoints work, tenant-scoped and authorization-checked
  exactly like B026's endpoints.
- Posting a shipping rate for a province that already has one updates its
  cost rather than creating a second row for that province.
- Creating a coupon with a duplicate code (case-insensitive) within the
  same tenant is rejected; the same code in a different tenant is
  allowed.
- Deactivating a coupon is reflected immediately in the list endpoint and
  (once B030 exists) makes that coupon fail validation at checkout.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the shipping-rate upsert, coupon creation
including the duplicate-code rejection, list, and deactivate. The full
existing IAM suite continues to pass unmodified.

Manual:

- As a seeded tenant member, set two shipping rates (including updating
  one), and create/list/deactivate a coupon via `curl`/an HTTP client.

# Lifecycle

Add row `B029` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B026`, and Spec link
`tasks/backend/B029-shipping-rate-and-coupon-admin-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/028-shop-checkout.md` is the permanent record and
is never deleted.
