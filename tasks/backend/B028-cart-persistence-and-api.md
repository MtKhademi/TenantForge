---
id: B028
slice: S27
title: Cart persistence and API
agent: backend-mentor
source: tasks/slices/027-shop-cart.md
---

# Objective

Add `ShopCart`/`ShopCartItem` persistence and the anonymous cart API: create
a cart, add an item (validating live stock), update an item's quantity,
remove an item, and fetch the current cart with a computed subtotal.

# Context

Read `tasks/slices/027-shop-cart.md` completely, including its explicit
statement that cart ownership is by opaque cart id only, with no customer
account anywhere in this module — every endpoint in this task is
anonymous, following the same `/api/shop/{tenantId}/...` prefix B027
introduced.

Read `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`
(created in B025) and the entity/map pattern it established before adding
the two new entities and maps.

# Scope

1. `domain/ShopCart.cs`, `ShopCartItem.cs` and their `IEntityTypeConfiguration`
   maps, added to `ShopDbContext`. One EF Core migration adding exactly
   these two tables.
2. `features/carts/CartsFeature.cs`:
   - `POST /api/shop/{tenantId}/carts` — creates an empty `ShopCart` for
     the tenant, returns `{ cartId }` (the cart's own `Id`, formatted as
     the canonical TSID string — this literal value is the cart token; no
     other field carries it).
   - `POST /api/shop/{tenantId}/carts/{cartId}/items` — body: `productVariantId`,
     `quantity`. Loads the variant, confirms it belongs to a product in
     this tenant and the product/variant chain is active, confirms
     `StockQuantity >= quantity` (read inside the same request/transaction
     as the insert — no separate "check then act" gap), inserts a
     `ShopCartItem` with `UnitPriceSnapshot` set to the variant's current
     effective price (`PriceOverride` if set, otherwise the product's
     `BasePrice`). If an item for the same `productVariantId` already
     exists in the cart, increase its quantity instead of inserting a
     second row (re-validate combined quantity against stock).
   - `PATCH /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` — body:
     `quantity`. Re-validates stock for the new quantity.
   - `DELETE /api/shop/{tenantId}/carts/{cartId}/items/{itemId}` — removes
     the item.
   - `GET /api/shop/{tenantId}/carts/{cartId}` — returns the cart's items
     (variant id, product name, color/size label, quantity, unit price)
     and the computed subtotal (`sum(UnitPriceSnapshot * Quantity)`). A
     `cartId` that does not exist, or belongs to a different tenant,
     returns `404`.
3. Stock validation must be safe under concurrency: use the database's own
   row-level guarantee (a single `UPDATE ... WHERE StockQuantity >= @qty`
   style guarded write, or an equivalent transaction with the correct
   isolation level) so two concurrent add-item calls against a variant
   with `StockQuantity = 1` never both succeed.

# Non-goals

- No authentication/authorization of any kind on any endpoint in this
  task — this is the deliberate guest-cart design stated in the slice.
- No cart expiry/cleanup job.
- No `ShopCoupon`/`ShopShippingRate` reference from this task (`ShopCart.CouponId`
  exists as a column per the slice but is never read or written here —
  only in B030).

# Acceptance

- All five endpoints behave as scoped above.
- A stock-race integration test: two concurrent add-item requests against
  a variant with `StockQuantity = 1` and `quantity = 1` each — exactly one
  succeeds, the other gets a clear "insufficient stock" error, and the
  variant's final `StockQuantity` in the database is never negative.
- Adding the same variant twice increases quantity rather than creating a
  duplicate cart-item row.
- `GET` on a cart from a different tenant's `{tenantId}` (or a nonexistent
  cart id) returns `404`.
- The subtotal returned by `GET` always equals the sum of
  `UnitPriceSnapshot * Quantity` across the cart's current items — proven
  by an integration test that adds two different-priced variants and
  checks the total.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the stock race, duplicate-variant quantity
merge, cross-tenant/nonexistent cart 404, and subtotal computation. The
full existing IAM suite continues to pass unmodified.

Manual:

- Create a cart, add two different variants, update one's quantity,
  remove the other, and confirm the final `GET` response matches.
- Attempt to add a quantity greater than `StockQuantity` and confirm a
  clear rejection.

# Lifecycle

Add row `B028` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B027`, and Spec link
`tasks/backend/B028-cart-persistence-and-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
