# B028 — Cart persistence and API

One backend slice: `ShopCart`/`ShopCartItem` persistence plus the anonymous
guest-cart API. A shopper gets a cart id, adds product variants (each add
reserves live stock immediately), adjusts quantities, removes items (releasing
stock), and sees a live subtotal — with no account and no Authorization
header anywhere.

## 1. Files changed and why

| File | Why |
| --- | --- |
| `src/modules/shop/.../domain/ShopCart.cs` | New entity: the cart row. Its TSID primary key **is** the opaque cart token the browser stores — no separate token field, no customer record. `CouponId` is a plain nullable column added now so B030 needs no additive schema change; `ApplyCoupon` is deliberately unused until then. |
| `src/modules/shop/.../domain/ShopCartItem.cs` | New entity: one line per (cart, variant). `UnitPriceSnapshot` is captured at add time so a later admin price edit never retroactively changes an open cart's total. |
| `src/modules/shop/.../infrastructure/ShopCartMap.cs`, `ShopCartItemMap.cs` | EF maps: `shop_carts`, `shop_cart_items`; TSID→`bigint` via the module's `ShopTsidValueConverter`; unique `(cart_id, product_variant_id)` index enforcing the one-line-per-variant rule; cascade delete with the cart, restrict against deleting a variant that is still in a cart. |
| `src/modules/shop/.../infrastructure/ShopDbContext.cs` | Registered the two new `DbSet`s and maps — the only way new entities enter the model. |
| `src/modules/shop/.../infrastructure/Migrations/20260917162655_AddShopCart*.cs` | **Generated** migration: the two tables, two FKs, the unique index. Review it as generated output. |
| `src/modules/shop/.../features/carts/CartContracts.cs` | The five public request/response records for the cart surface. |
| `src/modules/shop/.../features/carts/CartsFeature.cs` | The five endpoints (the slice's core). |
| `src/modules/shop/.../ShopModule.cs` | One line: `MapCartsFeature()` inside the existing `MapShopModule` — the module's activation seam maps all Shop endpoints, the host never does. |
| `tests/.../IamDbFixture.cs` | New `ShopCartDbFixture`/`ShopCartIsolatedCollection` on a dedicated database (`tenantforge_shop_cart_tests`) so cart stock mutations never disturb the B026/B027 databases' row-count assertions. |
| `tests/.../ShopCartIntegrationTests.cs` | Ten facts covering every acceptance line (below). |
| `tests/.../ShopModuleIntegrationTests.cs` | One honest, non-weakening edit: `ExpectedShopTables` hardcodes "exactly the Shop-owned tables" for the B025 startup test — that set is now eight, not six. Any future task adding a Shop table must update the array in the same change. |

## 2. Request flow (add item — the interesting one)

`POST /api/shop/{tenantId}/carts/{cartId}/items`

1. Parse `tenantId`, `cartId`, `productVariantId` through
   `TsidId.TryParse`; any malformed id → `404`; `quantity <= 0` → `400`
   naming the field. Nothing touches the database yet.
2. Tenant-scoped cart existence check (`cart.Id == @cartId && cart.TenantId ==
   @tenantId`) → `404` otherwise. This is the whole "authorization": the cart
   id alone names it, but the row must belong to the route's tenant.
3. Tenant-scoped, active-product join read of the variant: returns its
   `EffectivePrice = PriceOverride ?? BasePrice` for the snapshot, and a
   `404` if the variant is unknown, another tenant's, or the product is
   inactive.
4. **The race-safe reservation:**
   `db.ProductVariants.Where(v => v.Id == @id && v.StockQuantity >= @qty)
   .ExecuteUpdateAsync(set StockQuantity -= @qty)`. EF translates this to one
   guarded `UPDATE ... WHERE stock_quantity >= @qty`. Under concurrency,
   PostgreSQL evaluates the WHERE against each row version as it commits;
   the second request against the last unit sees `0 >= 1` is false, updates
   **zero** rows, and the handler answers `409` Problem
   "Insufficient stock". No explicit lock, transaction or retry is needed
   for the decrement itself — the database's row-level guarantee is the
   mechanism, and the stock can never go negative.
5. Insert the line or, if a line for that variant exists, merge the quantity
   into it (backed by the unique index), then `SaveChangesAsync`.
6. `BuildCartResponseAsync`: one tenant-scoped join over cart items →
   variants → products, projecting the response rows and summing
   `unitPrice × quantity` on the server for the subtotal.

Create/delete/get are the same shape minus the reservation; delete runs the
release (`stock += item.Quantity`) **before** removing the row.

## 3. Backend concepts introduced

- **Conditional `UPDATE` as a concurrency primitive.** A guarded
  `ExecuteUpdateAsync` is the smallest correct answer to "never oversell":
  the check and the change are one statement. Contrast with the old
  in-memory `ShopProductVariant.TryReserve` (still present, still unused):
  it works for one process reading a tracked entity, but two processes could
  both pass the in-memory check — the WHERE clause is what makes it safe
  across processes.
- **Reservation semantics (a decision, reused by B031).** Stock is
  decremented at add-to-cart time and released on remove/decrease. B031's
  order creation therefore only *converts* the already-reserved cart into a
  permanent order; it must not decrement stock a second time. Known,
  deliberate limitation: an abandoned cart's reservation is never released
  automatically — there is no expiry job, by design, until a real product
  need names one.
- **Price snapshotting.** The cart stores the price at add time
  (`UnitPriceSnapshot`), never re-reading the live product price, so the
  subtotal is stable while the cart is open and is exactly
  `Σ UnitPriceSnapshot × Quantity`.
- **Opaque-token ownership.** The 13-character Crockford TSID is the only
  credential a guest has. It is unguessable in practice, and every query is
  additionally scoped by the route's `{tenantId}`, so even a guessed id from
  one tenant cannot reach another tenant's carts.

## 4. Security decisions

- **Anonymous by design, not by accident.** No `.RequireAuthorization()` and
  no `.AllowAnonymous()` on any cart route: this host registers only the
  named `PlatformAdmin` policy and has no `DefaultPolicy`/`FallbackPolicy`,
  so a route with no authorization metadata is simply not challenged (the
  same proven mechanism as B027's storefront routes). The integration fact
  `AllCartRoutes_AreAnonymous_NoAuthorizationHeaderRequired` locks this with
  a bare client asserting `Authorization` is null.
- **Default-deny on identity.** Every id is parsed or 404'd; every
  existence check is tenant-scoped; cross-tenant and unknown-cart GETs
  return an identical `404` (no existence leak), and malformed ids are `404`,
  never `400`/`500`.
- **Failures cost nothing but a status code.** The insufficient-stock `409`
  carries a fixed title/detail; no logs in this slice contain ids, prices or
  tokens beyond ordinary request logging.
- **Server-side stock law.** The client's idea of available stock is never
  trusted; only the variant row's current `StockQuantity` in the guarded
  UPDATE decides.

## 5. Alternatives deliberately postponed

- **Explicit transaction around reserve + insert.** The two writes
  (stock UPDATE, cart-item insert) are not in one transaction: if the
  process died between them, the reservation would leak until the cart's
  row is deleted or the item removed. That is the same class of leak the
  no-expiry decision already accepts, and a transaction plus compensation is
  a larger design question for the order-creation slice (B031), where the
  whole cart is committed atomically.
- **Cart expiry / background cleanup job.** Named non-goal: it would add a
  hosted service and a retention policy this slice cannot demonstrate.
- **Per-variant `SELECT ... FOR UPDATE` pessimistic locking.** Unnecessary
  once the decrement is a guarded conditional UPDATE.
- **A `TenantForge.Modules.Shop.Contract` project or a `docs/modules/Shop.md`
  handbook.** Neither exists yet by deliberate admission rules; the cart
  contracts stay module-local.
- **Removing the now-unused `ShopProductVariant.TryReserve`.** Not in this
  Spec's file list; it is dead-but-harmless and belongs to a later
  cleanup.

## 6. Verify it

```bash
# build + all tests (143 total, 10 of them B028's)
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo

# focused:
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo \
  --filter "FullyQualifiedName~ShopCartIntegrationTests"
```

Manual (Development, dev Postgres running — exactly what the demo did):

```bash
# 1. log in as the seeded admin, create a tenant, a category, and a product
#    whose single variant has stockQuantity: 1 (B026 admin API).
# 2. anonymous:
curl -X POST http://localhost:5080/api/shop/<tenantId>/carts            # 201 {"cartId":...}
curl -X POST http://localhost:5080/api/shop/<tenantId>/carts/<cartId>/items \
  -H "Content-Type: application/json" \
  -d '{"productVariantId":"<variantId>","quantity":1}'                   # 200, subTotal 75000.00
# 3. run the exact same add again:                                     # 409 {"title":"Insufficient stock"}
# 4. GET the cart                                                      # 200, one line
# 5. GET the product detail                                            # variant stockQuantity == 0
# 6. GET /api/shop/not-a-tsid/carts/<cartId>                           # 404
```

## 7. Three review questions

1. In `TwoConcurrentAdds_AgainstTheLastUnit_ExactlyOneWins...`, the two
   requests are sent "concurrently" but are really two sequential
   `HttpClient` calls — why does the test still prove the guard, and what
   would you have to add to make it a *true* parallelism test? (Hint:
   `Task.WhenAll` — and what would that still not prove about two
   *processes*?)
2. `BuildCartResponseAsync` sums the subtotal in LINQ after a single
   `ToListAsync()`. When would that stop being acceptable, and what is the
   cheapest alternative that keeps the answer in the database?
3. The add-item flow does: guarded UPDATE, then `SaveChangesAsync` for the
   cart item — two commits. Sketch the failure window and explain why the
   Spec's reservation semantics make this acceptable now, but something B031
   must handle when a cart becomes an order.
