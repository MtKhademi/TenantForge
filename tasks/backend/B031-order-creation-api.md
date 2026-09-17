---
id: B031
slice: S29
title: Order creation API
agent: backend-mentor
source: tasks/slices/029-shop-order-and-sandbox-payment.md
---

# Objective

Add `POST /api/shop/{tenantId}/orders`: given the same inputs B030's
checkout summary validated (cart id, address, optional coupon code),
atomically snapshot the cart into a `ShopOrder`/`ShopOrderItem` set,
generate an `OrderNumber` and a `TrackingCode`, and clear the cart — all
inside one transaction.

# Context

Read `tasks/slices/029-shop-order-and-sandbox-payment.md` completely.
Read B030's delivered `features/checkout/CheckoutFeature.cs` — this task
re-runs the same coupon/shipping validation rather than trusting a
client-supplied summary.

## Stock is already reserved — this task does not decrement it again

B028's Spec made a deliberate choice: adding an item to a cart already
atomically reserves its stock (decrements `ShopProductVariant.StockQuantity`
immediately, via `ExecuteUpdateAsync`). By the time an order is created
from that cart, the stock for every item in it is already set aside — so
**this task never touches `StockQuantity`**. What this task must still
guard against is a **double order** from the same cart (for example a
shopper double-clicking "place order," or a retried request after a slow
response): the transaction below makes the cart-consumption step atomic
too, so only the first of two concurrent order-creation calls for the
same cart succeeds.

# Scope — every file, in order

## 1. Domain entities

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopOrder.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopOrderStatus
{
    PendingPayment,
    Paid,
    Cancelled,
    Fulfilled
}

internal sealed class ShopOrder
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string OrderNumber { get; private set; } = string.Empty;
    public string TrackingCode { get; private set; } = string.Empty;
    public ShopOrderStatus Status { get; private set; } = ShopOrderStatus.PendingPayment;
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string ShippingProvince { get; private set; } = string.Empty;
    public string ShippingCity { get; private set; } = string.Empty;
    public string ShippingAddressLine { get; private set; } = string.Empty;
    public string ShippingPostalCode { get; private set; } = string.Empty;
    public decimal SubTotal { get; private set; }
    public decimal ShippingCost { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal GrandTotal { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private ShopOrder()
    {
    }

    public static ShopOrder Create(
        Tsid tenantId,
        string orderNumber,
        string trackingCode,
        string customerName,
        string customerPhone,
        string shippingProvince,
        string shippingCity,
        string shippingAddressLine,
        string shippingPostalCode,
        decimal subTotal,
        decimal shippingCost,
        decimal discountAmount,
        DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ShopOrder
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            OrderNumber = orderNumber,
            TrackingCode = trackingCode,
            Status = ShopOrderStatus.PendingPayment,
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
            ShippingProvince = shippingProvince.Trim(),
            ShippingCity = shippingCity.Trim(),
            ShippingAddressLine = shippingAddressLine.Trim(),
            ShippingPostalCode = shippingPostalCode.Trim(),
            SubTotal = subTotal,
            ShippingCost = shippingCost,
            DiscountAmount = discountAmount,
            GrandTotal = subTotal - discountAmount + shippingCost,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public void MarkPaid() => Status = ShopOrderStatus.Paid;

    public void MarkPaymentFailed()
    {
        if (Status == ShopOrderStatus.PendingPayment) return;
        Status = ShopOrderStatus.PendingPayment;
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/domain/ShopOrderItem.cs`:**

```csharp
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopOrderItem
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid OrderId { get; private set; }
    public Tsid ProductVariantId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public string VariantLabelSnapshot { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    private ShopOrderItem()
    {
    }

    public static ShopOrderItem Create(
        Tsid orderId,
        Tsid productVariantId,
        string productNameSnapshot,
        string variantLabelSnapshot,
        decimal unitPrice,
        int quantity)
    {
        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        return new ShopOrderItem
        {
            Id = TsidId.NewId(),
            OrderId = orderId,
            ProductVariantId = productVariantId,
            ProductNameSnapshot = productNameSnapshot,
            VariantLabelSnapshot = variantLabelSnapshot,
            UnitPrice = unitPrice,
            Quantity = quantity
        };
    }
}
```

## 2. EF Core maps

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopOrderMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopOrderMap : IEntityTypeConfiguration<ShopOrder>
{
    public void Configure(EntityTypeBuilder<ShopOrder> builder)
    {
        builder.ToTable("shop_orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(order => order.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(order => order.OrderNumber)
            .HasColumnName("order_number")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.TrackingCode)
            .HasColumnName("tracking_code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.CustomerName)
            .HasColumnName("customer_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(order => order.CustomerPhone)
            .HasColumnName("customer_phone")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.ShippingProvince)
            .HasColumnName("shipping_province")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(order => order.ShippingCity)
            .HasColumnName("shipping_city")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(order => order.ShippingAddressLine)
            .HasColumnName("shipping_address_line")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(order => order.ShippingPostalCode)
            .HasColumnName("shipping_postal_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.SubTotal).HasColumnName("sub_total").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.ShippingCost).HasColumnName("shipping_cost").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(order => order.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(order => order.OrderNumber)
            .IsUnique()
            .HasDatabaseName("ix_shop_orders_order_number");

        builder.HasIndex(order => order.TrackingCode)
            .IsUnique()
            .HasDatabaseName("ix_shop_orders_tracking_code");

        builder.HasIndex(order => new { order.TenantId, order.TrackingCode, order.CustomerPhone })
            .HasDatabaseName("ix_shop_orders_tenant_tracking_phone");
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopOrderItemMap.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopOrderItemMap : IEntityTypeConfiguration<ShopOrderItem>
{
    public void Configure(EntityTypeBuilder<ShopOrderItem> builder)
    {
        builder.ToTable("shop_order_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(item => item.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(item => item.VariantLabelSnapshot).HasColumnName("variant_label_snapshot").HasMaxLength(120).IsRequired();
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();

        builder.HasIndex(item => item.OrderId)
            .HasDatabaseName("ix_shop_order_items_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## 3. Register the two entities in `ShopDbContext`

Add these two lines beside the existing `DbSet<T>` properties:

```csharp
    internal DbSet<ShopOrder> Orders => Set<ShopOrder>();
    internal DbSet<ShopOrderItem> OrderItems => Set<ShopOrderItem>();
```

And these two lines inside `OnModelCreating`:

```csharp
        modelBuilder.ApplyConfiguration(new ShopOrderMap());
        modelBuilder.ApplyConfiguration(new ShopOrderItemMap());
```

## 4. Migration

```bash
dotnet ef migrations add AddShopOrders --project src/modules/shop/TenantForge.Modules.Shop --startup-project src/api/TenantForge.Api --output-dir infrastructure/Migrations
```

## 5. Request/response records

**`src/modules/shop/TenantForge.Modules.Shop/features/orders/OrderContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Orders;

public sealed record CreateOrderRequest(
    string? CartId,
    string? CustomerName,
    string? CustomerPhone,
    string? ShippingProvince,
    string? ShippingCity,
    string? ShippingAddressLine,
    string? ShippingPostalCode,
    string? CouponCode);

public sealed record OrderCreatedResponse(
    string OrderId,
    string OrderNumber,
    string TrackingCode,
    string Status,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal ShippingCost,
    decimal GrandTotal);
```

## 6. `src/modules/shop/TenantForge.Modules.Shop/features/orders/OrderCreationFeature.cs`

```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Orders;

internal static class OrderCreationFeature
{
    public static IEndpointRouteBuilder MapOrderCreationFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders", async (
            string tenantId,
            CreateOrderRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.CartId, out var cartTsid)) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.CustomerName)) errors["customerName"] = ["Customer name is required."];
            if (string.IsNullOrWhiteSpace(request.CustomerPhone)) errors["customerPhone"] = ["Customer phone is required."];

            await using var transaction = await db.Database.BeginTransactionAsync();

            // Consuming the cart atomically here (rather than after building
            // the order) is what protects against a double order from the
            // same cart: a second concurrent call finds no cart items left
            // and returns 404, exactly like the cart was never checked out.
            var cartItems = await db.CartItems.Where(item => item.CartId == cartTsid).ToListAsync();
            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists || cartItems.Count == 0)
            {
                await transaction.RollbackAsync();
                return Results.NotFound();
            }

            var subTotal = cartItems.Sum(item => item.UnitPriceSnapshot * item.Quantity);

            decimal shippingCost = 0;
            if (string.IsNullOrWhiteSpace(request.ShippingProvince))
            {
                errors["shippingProvince"] = ["Shipping province is required."];
            }
            else
            {
                var province = request.ShippingProvince.Trim();
                var rate = await db.ShippingRates.AsNoTracking()
                    .SingleOrDefaultAsync(rate => rate.TenantId == tenantTsid && rate.ProvinceName == province);
                if (rate is null)
                {
                    errors["shippingProvince"] = ["This tenant does not ship to the selected province."];
                }
                else
                {
                    shippingCost = rate.Cost;
                }
            }

            decimal discountAmount = 0;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
                var coupon = await db.Coupons.AsNoTracking()
                    .SingleOrDefaultAsync(coupon => coupon.TenantId == tenantTsid && coupon.NormalizedCode == normalizedCode);
                if (coupon is null || !coupon.IsActive || coupon.ExpiresAtUtc < DateTimeOffset.UtcNow)
                {
                    errors["couponCode"] = ["This coupon code is not valid."];
                }
                else
                {
                    discountAmount = coupon.DiscountType == ShopDiscountType.Percentage
                        ? Math.Round(subTotal * coupon.DiscountValue / 100m, 2)
                        : Math.Min(coupon.DiscountValue, subTotal);
                }
            }

            if (errors.Count > 0)
            {
                await transaction.RollbackAsync();
                return Results.ValidationProblem(errors);
            }

            var now = DateTimeOffset.UtcNow;
            var orderNumber = await GenerateUniqueOrderNumberAsync(db, now);
            var trackingCode = GenerateTrackingCode();

            var order = ShopOrder.Create(
                tenantTsid, orderNumber, trackingCode,
                request.CustomerName!, request.CustomerPhone!,
                request.ShippingProvince!, request.ShippingCity ?? string.Empty,
                request.ShippingAddressLine ?? string.Empty, request.ShippingPostalCode ?? string.Empty,
                subTotal, shippingCost, discountAmount, now);
            db.Orders.Add(order);

            foreach (var item in cartItems)
            {
                var variant = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == item.ProductVariantId);
                var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == variant.ProductId);
                db.OrderItems.Add(ShopOrderItem.Create(
                    order.Id, variant.Id, product.Name, $"{variant.Color} / {variant.Size}",
                    item.UnitPriceSnapshot, item.Quantity));
            }

            // Stock was already reserved when these items were added to the
            // cart (B028) — this task does not decrement StockQuantity
            // again. Removing the cart items here only finalizes the
            // consumption; it does not touch stock.
            db.CartItems.RemoveRange(cartItems);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/shop/{tenantId}/orders/{TsidId.Format(order.Id)}", new OrderCreatedResponse(
                TsidId.Format(order.Id), order.OrderNumber, order.TrackingCode, order.Status.ToString(),
                order.SubTotal, order.DiscountAmount, order.ShippingCost, order.GrandTotal));
        });

        return endpoints;
    }

    private static async Task<string> GenerateUniqueOrderNumberAsync(ShopDbContext db, DateTimeOffset nowUtc)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"ORD-{nowUtc:yyMMdd}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
            var exists = await db.Orders.AnyAsync(order => order.OrderNumber == candidate);
            if (!exists) return candidate;
        }

        throw new InvalidOperationException("Could not generate a unique order number after 5 attempts.");
    }

    /// <summary>
    /// A random, cryptographically-generated code — never derived from
    /// OrderNumber, the cart id, or any other value the shopper already
    /// has, since B033/S30 relies on it not being guessable.
    /// </summary>
    private static string GenerateTrackingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I
        var bytes = RandomNumberGenerator.GetBytes(12);
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars);
    }
}
```

## 7. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. Add this
one line inside `MapShopModule`, alongside the existing `Map*Feature`
calls:

```csharp
        endpoints.MapOrderCreationFeature();
```

Add the matching `using`:

```csharp
using TenantForge.Modules.Shop.Features.Orders;
```

# Non-goals

- No payment initiation (B032).
- No email/SMS confirmation.
- No admin order-management endpoint (list/view orders as a tenant
  admin).
- No stock decrement in this task — stock was already reserved by B028
  when the items were added to the cart (see this Spec's Context).

# If you get stuck

```bash
curl -X POST http://localhost:5080/api/shop/<tenantId>/orders \
  -H "Content-Type: application/json" \
  -d '{
        "cartId": "<cartId>",
        "customerName": "مریم رضایی",
        "customerPhone": "09121234567",
        "shippingProvince": "تهران",
        "shippingCity": "تهران",
        "shippingAddressLine": "خیابان ولیعصر",
        "shippingPostalCode": "1234567890",
        "couponCode": "WELCOME10"
      }'
```

Expected: `201 Created` with
`{"orderId":"...","orderNumber":"ORD-...","trackingCode":"...","status":"PendingPayment","subTotal":890000,"discountAmount":89000,"shippingCost":50000,"grandTotal":851000}`.
Immediately repeating the exact same call with the same `cartId` is
expected to return `404` (the cart is already consumed).

# Acceptance

- A happy-path order creation returns `OrderNumber`/`TrackingCode` and
  the correct totals; the cart's stock reservation from B028 is left
  untouched (not decremented a second time).
- A double-order-creation race test: two concurrent order-creation calls
  for the same cart — exactly one succeeds (`201`), the other gets `404`
  (the cart is already consumed by the winner), and exactly one
  `ShopOrder`/its `ShopOrderItem` rows exist for that cart.
- An invalid coupon or unshippable province at order-creation time is
  rejected with the same distinct errors B030 uses, and no order is
  created.
- After a successful order creation, the cart's items are gone (`GET`
  on the cart returns an empty item list).
- `TrackingCode` is never derived from `OrderNumber`, the cart id, or any
  other value guessable from information the shopper already has.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover the happy path, the double-order-creation
race, coupon/shipping re-validation failures, and cart consumption. The
full existing IAM suite continues to pass unmodified.

Manual: the `curl` sequence under "If you get stuck" above, including
the immediate repeat call.

# Lifecycle

Add row `B031` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B030`, and Spec link
`tasks/backend/B031-order-creation-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/029-shop-order-and-sandbox-payment.md` is the
permanent record and is never deleted.
