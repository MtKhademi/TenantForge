---
id: B028
slice: S27
title: Cart persistence and API
agent: backend-mentor
source: tasks/slices/027-shop-cart.md
---

# Objective

Add `ShopCart`/`ShopCartItem` persistence and the anonymous cart API:
create a cart, add an item (reserving live stock atomically), update an
item's quantity, remove an item (releasing its reservation), and fetch
the current cart with a computed subtotal.

# Context

Read `tasks/slices/027-shop-cart.md` completely. Read B027's delivered
`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`
before adding the two new entities and maps.

## A decision this task makes, stated once, reused by B031

**Adding an item to a cart reserves its stock immediately** —
`ShopProductVariant.StockQuantity` is decremented atomically the moment
an item is added or its quantity is increased, and incremented back
(released) when an item is removed or its quantity is decreased. This is
what makes "two concurrent add-item calls against `StockQuantity = 1`
never both succeed" (this task's own Acceptance criterion) possible: if
stock were only *checked* at add-time and decremented later at order
creation, two different shoppers' carts could both hold the last unit at
once with nothing to stop it. Because stock is reserved here, **B031
(order creation) does not decrement stock a second time** — it only
converts an already-reserved cart into a permanent order. State this
explicitly in B031 if you implement it later; this Spec's Non-goals
below also says it. An abandoned cart's reservation is never released
automatically (there is no cart-expiry job — see Non-goals); that is a
known, deliberate limitation, not a bug to fix in this task.

Every stock change in this task uses EF Core's `ExecuteUpdateAsync`,
which translates to one atomic, row-guarded `UPDATE ... WHERE ...`
statement — this is the "database's own row-level guarantee" the
original Spec called for, and it needs no explicit transaction or lock
statement to be race-safe.

# Scope — every file, in order

## 1. Domain entities

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopCart.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCart
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid? CouponId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private ShopCart()
    {
    }

    public static ShopCart Create(Tsid tenantId, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ShopCart
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            CouponId = null,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    /// <summary>Set in B030's checkout-summary task; unused until then.</summary>
    public void ApplyCoupon(Tsid? couponId) => CouponId = couponId;
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopCartItem.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCartItem
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid CartId { get; private set; }
    public Tsid ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }

    private ShopCartItem()
    {
    }

    public static ShopCartItem Create(Tsid cartId, Tsid productVariantId, int quantity, decimal unitPriceSnapshot)
    {
        if (TsidId.IsDefault(cartId))
        {
            throw new ArgumentException("Cart id is required.", nameof(cartId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        return new ShopCartItem
        {
            Id = TsidId.NewId(),
            CartId = cartId,
            ProductVariantId = productVariantId,
            Quantity = quantity,
            UnitPriceSnapshot = unitPriceSnapshot
        };
    }

    public void SetQuantity(int quantity) => Quantity = quantity;
}
```

## 2. EF Core maps

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopCartMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCartMap : IEntityTypeConfiguration<ShopCart>
{
    public void Configure(EntityTypeBuilder<ShopCart> builder)
    {
        builder.ToTable("shop_carts");

        builder.HasKey(cart => cart.Id);

        builder.Property(cart => cart.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(cart => cart.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cart => cart.CouponId)
            .HasColumnName("coupon_id")
            .HasConversion(ShopTsidValueConverter.Shared);

        builder.Property(cart => cart.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopCartItemMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCartItemMap : IEntityTypeConfiguration<ShopCartItem>
{
    public void Configure(EntityTypeBuilder<ShopCartItem> builder)
    {
        builder.ToTable("shop_cart_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(item => item.CartId)
            .HasColumnName("cart_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(item => item.UnitPriceSnapshot)
            .HasColumnName("unit_price_snapshot")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.HasIndex(item => new { item.CartId, item.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("ix_shop_cart_items_cart_variant");

        builder.HasOne<ShopCart>()
            .WithMany()
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ShopProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

## 3. Register the two entities in `ShopDbContext`

Edit `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`.
It currently reads:

```csharp
    internal DbSet<ShopCategory> Categories => Set<ShopCategory>();
    internal DbSet<ShopProduct> Products => Set<ShopProduct>();
    internal DbSet<ShopProductVariant> ProductVariants => Set<ShopProductVariant>();
    internal DbSet<ShopSizeGuideColumn> SizeGuideColumns => Set<ShopSizeGuideColumn>();
    internal DbSet<ShopSizeGuideRow> SizeGuideRows => Set<ShopSizeGuideRow>();
    internal DbSet<ShopSizeGuideCell> SizeGuideCells => Set<ShopSizeGuideCell>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopCategoryMap());
        modelBuilder.ApplyConfiguration(new ShopProductMap());
        modelBuilder.ApplyConfiguration(new ShopProductVariantMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideColumnMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideRowMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideCellMap());
    }
```

Change it to:

```csharp
    internal DbSet<ShopCategory> Categories => Set<ShopCategory>();
    internal DbSet<ShopProduct> Products => Set<ShopProduct>();
    internal DbSet<ShopProductVariant> ProductVariants => Set<ShopProductVariant>();
    internal DbSet<ShopSizeGuideColumn> SizeGuideColumns => Set<ShopSizeGuideColumn>();
    internal DbSet<ShopSizeGuideRow> SizeGuideRows => Set<ShopSizeGuideRow>();
    internal DbSet<ShopSizeGuideCell> SizeGuideCells => Set<ShopSizeGuideCell>();
    internal DbSet<ShopCart> Carts => Set<ShopCart>();
    internal DbSet<ShopCartItem> CartItems => Set<ShopCartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopCategoryMap());
        modelBuilder.ApplyConfiguration(new ShopProductMap());
        modelBuilder.ApplyConfiguration(new ShopProductVariantMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideColumnMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideRowMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideCellMap());
        modelBuilder.ApplyConfiguration(new ShopCartMap());
        modelBuilder.ApplyConfiguration(new ShopCartItemMap());
    }
```

## 4. Migration

```bash
dotnet ef migrations add AddShopCart --project src/modules/shop/TenantForge.Modules.Shop --startup-project src/api/TenantForge.Api --output-dir infrastructure/Migrations
```

## 5. Request/response records

**`src/modules/shop/TenantForge.Modules.Shop/features/carts/CartContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Carts;

public sealed record CreateCartResponse(string CartId);

public sealed record AddCartItemRequest(string? ProductVariantId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartItemResponse(
    string Id,
    string ProductVariantId,
    string ProductName,
    string VariantLabel,
    int Quantity,
    decimal UnitPrice);

public sealed record CartResponse(
    string CartId,
    IReadOnlyList<CartItemResponse> Items,
    decimal SubTotal);
```

## 6. `src/modules/shop/TenantForge.Modules.Shop/features/carts/CartsFeature.cs`

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Carts;

internal static class CartsFeature
{
    public static IEndpointRouteBuilder MapCartsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/carts", async (string tenantId, ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var cart = ShopCart.Create(tenantTsid, DateTimeOffset.UtcNow);
            db.Carts.Add(cart);
            await db.SaveChangesAsync();

            return Results.Created($"/api/shop/{tenantId}/carts/{TsidId.Format(cart.Id)}", new CreateCartResponse(TsidId.Format(cart.Id)));
        });

        endpoints.MapPost("/api/shop/{tenantId}/carts/{cartId}/items", async (
            string tenantId,
            string cartId,
            AddCartItemRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.ProductVariantId, out var variantTsid) || request.Quantity <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["productVariantId"] = ["A valid product variant is required."],
                });
            }

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            var variant = await db.ProductVariants.AsNoTracking()
                .Join(db.Products.AsNoTracking().Where(product => product.TenantId == tenantTsid && product.IsActive),
                    variant => variant.ProductId, product => product.Id,
                    (variant, product) => new { variant.Id, variant.StockQuantity, product.Name, variant.Color, variant.Size, EffectivePrice = variant.PriceOverride ?? product.BasePrice })
                .SingleOrDefaultAsync(row => row.Id == variantTsid);
            if (variant is null) return Results.NotFound();

            var existingItem = await db.CartItems.SingleOrDefaultAsync(item =>
                item.CartId == cartTsid && item.ProductVariantId == variantTsid);

            // Atomic, race-safe reservation: the WHERE clause and the
            // decrement happen in one guarded UPDATE statement, so two
            // concurrent requests against the same variant can never both
            // succeed past the available quantity (see this Spec's Context).
            var reserved = await db.ProductVariants
                .Where(v => v.Id == variantTsid && v.StockQuantity >= request.Quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.StockQuantity, v => v.StockQuantity - request.Quantity));

            if (reserved == 0) return InsufficientStockProblem();

            if (existingItem is null)
            {
                db.CartItems.Add(ShopCartItem.Create(cartTsid, variantTsid, request.Quantity, variant.EffectivePrice));
            }
            else
            {
                existingItem.SetQuantity(existingItem.Quantity + request.Quantity);
            }

            await db.SaveChangesAsync();

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid));
        });

        endpoints.MapPatch("/api/shop/{tenantId}/carts/{cartId}/items/{itemId}", async (
            string tenantId,
            string cartId,
            string itemId,
            UpdateCartItemRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid) || !TsidId.TryParse(itemId, out var itemTsid))
            {
                return Results.NotFound();
            }

            if (request.Quantity <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quantity"] = ["Quantity must be positive; remove the item instead of setting it to zero."],
                });
            }

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            var item = await db.CartItems.SingleOrDefaultAsync(item => item.Id == itemTsid && item.CartId == cartTsid);
            if (item is null) return Results.NotFound();

            var delta = request.Quantity - item.Quantity;
            if (delta > 0)
            {
                var reserved = await db.ProductVariants
                    .Where(v => v.Id == item.ProductVariantId && v.StockQuantity >= delta)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.StockQuantity, v => v.StockQuantity - delta));
                if (reserved == 0) return InsufficientStockProblem();
            }
            else if (delta < 0)
            {
                await db.ProductVariants
                    .Where(v => v.Id == item.ProductVariantId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.StockQuantity, v => v.StockQuantity - delta));
            }

            item.SetQuantity(request.Quantity);
            await db.SaveChangesAsync();

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid));
        });

        endpoints.MapDelete("/api/shop/{tenantId}/carts/{cartId}/items/{itemId}", async (
            string tenantId,
            string cartId,
            string itemId,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid) || !TsidId.TryParse(itemId, out var itemTsid))
            {
                return Results.NotFound();
            }

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            var item = await db.CartItems.SingleOrDefaultAsync(item => item.Id == itemTsid && item.CartId == cartTsid);
            if (item is null) return Results.NotFound();

            // Release the reservation before removing the row.
            await db.ProductVariants
                .Where(v => v.Id == item.ProductVariantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.StockQuantity, v => v.StockQuantity + item.Quantity));

            db.CartItems.Remove(item);
            await db.SaveChangesAsync();

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid));
        });

        endpoints.MapGet("/api/shop/{tenantId}/carts/{cartId}", async (string tenantId, string cartId, ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid)) return Results.NotFound();

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid));
        });

        return endpoints;
    }

    private static async Task<CartResponse> BuildCartResponseAsync(ShopDbContext db, Tsid tenantId, Tsid cartId)
    {
        var rows = await db.CartItems.AsNoTracking()
            .Where(item => item.CartId == cartId)
            .Join(db.ProductVariants.AsNoTracking(), item => item.ProductVariantId, variant => variant.Id,
                (item, variant) => new { item, variant })
            .Join(db.Products.AsNoTracking().Where(product => product.TenantId == tenantId), row => row.variant.ProductId, product => product.Id,
                (row, product) => new CartItemResponse(
                    TsidId.Format(row.item.Id),
                    TsidId.Format(row.variant.Id),
                    product.Name,
                    $"{row.variant.Color} / {row.variant.Size}",
                    row.item.Quantity,
                    row.item.UnitPriceSnapshot))
            .ToListAsync();

        var subTotal = rows.Sum(row => row.UnitPrice * row.Quantity);
        return new CartResponse(TsidId.Format(cartId), rows, subTotal);
    }

    private static IResult InsufficientStockProblem() => Results.Problem(
        title: "Insufficient stock",
        detail: "The requested quantity is no longer available for this variant.",
        statusCode: StatusCodes.Status409Conflict);
}
```

## 7. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. `MapShopModule`
currently reads:

```csharp
    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
        endpoints.MapStorefrontCatalogFeature();
    }
```

Change it to:

```csharp
    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
        endpoints.MapStorefrontCatalogFeature();
        endpoints.MapCartsFeature();
    }
```

Add the matching `using`:

```csharp
using TenantForge.Modules.Shop.Features.Carts;
```

# Non-goals

- No authentication/authorization of any kind on any endpoint in this
  task — this is the deliberate guest-cart design stated in the slice.
- No cart expiry/cleanup job — an abandoned cart's stock reservation is
  never released automatically.
- No `ShopCoupon`/`ShopShippingRate` reference from this task
  (`ShopCart.CouponId` exists as a column and the `ApplyCoupon` method
  above per the slice, but nothing in this task calls it — only B030
  does).
- B031 (order creation) does not decrement stock a second time — see
  this Spec's Context.

# If you get stuck

**Testing the stock reservation manually.** Using a product variant with
`stockQuantity: 1` created via B026's admin API:

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/carts
```

Expected: `201 Created`, body `{"cartId":"<newCartId>"}`.

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/carts/<cartId>/items \
  -H "Content-Type: application/json" \
  -d '{"productVariantId":"<variantId>","quantity":1}'
```

Expected: `200 OK` with the cart's items and subtotal. Run the exact same
`curl` command again immediately (simulating a second shopper): expect
`409 Conflict` with `{"title":"Insufficient stock", ...}`, since the first
call already reserved the only unit.

```bash
curl http://localhost:5080/api/shop/<tenantId>/carts/<cartId>
```

Expected: `200 OK`, same items/subtotal as the add-item response.

# Acceptance

- All five endpoints behave as scoped above.
- A stock-race integration test: two concurrent add-item requests against
  a variant with `StockQuantity = 1` and `quantity = 1` each — exactly one
  succeeds (`200`), the other gets `409` with the insufficient-stock
  problem, and the variant's final `StockQuantity` in the database is
  `0`, never negative.
- Adding the same variant twice increases quantity rather than creating a
  duplicate cart-item row, and reserves the additional quantity.
- Removing an item releases its reserved stock back onto the variant.
- `GET` on a cart from a different tenant's `{tenantId}` (or a
  nonexistent cart id) returns `404`.
- The subtotal returned by `GET` always equals the sum of
  `UnitPriceSnapshot * Quantity` across the cart's current items.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the stock race, duplicate-variant quantity
merge and reservation, item removal releasing stock, cross-tenant/
nonexistent cart 404, and subtotal computation. The full existing IAM
suite continues to pass unmodified.

Manual: the `curl` sequence under "If you get stuck" above.

# Lifecycle

Add row `B028` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B027`, and Spec link
`tasks/backend/B028-cart-persistence-and-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/027-shop-cart.md` is the permanent record and is
never deleted.
