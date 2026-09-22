using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Orders;

internal static class OrderCreationFeature
{
    public static IEndpointRouteBuilder MapOrderCreationFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders", async (
            string tenantId,
            CreateOrderRequest request,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.CartId, out var cartTsid)) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.CustomerName)) errors["customerName"] = ["Customer name is required."];
            if (string.IsNullOrWhiteSpace(request.CustomerPhone)) errors["customerPhone"] = ["Customer phone is required."];

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (CartsFeature.IsExpired(lease)) return CartsFeature.CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var cart = await db.Carts
                .FromSqlRaw("""
                    SELECT *
                    FROM shop_carts
                    WHERE tenant_id = {0} AND id = {1}
                    FOR UPDATE
                    """, tenantTsid.ToLong(), cartTsid.ToLong())
                .SingleOrDefaultAsync(ct);
            if (cart is null) return Results.NotFound();
            if (cart.Status == ShopCartStatus.Expired) return CartsFeature.CartExpiredProblem();
            if (cart.Status == ShopCartStatus.Converted) return Results.NotFound();

            // Consuming the cart atomically here (rather than after building
            // the order) is what protects against a double order from the
            // same cart: a second concurrent call finds no active cart items left
            // and returns 404, exactly like the cart was never checked out.
            var cartItems = await db.CartItems.Where(item => item.CartId == cartTsid).ToListAsync(ct);
            if (cartItems.Count == 0)
            {
                await transaction.RollbackAsync(ct);
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
                await transaction.RollbackAsync(ct);
                return Results.ValidationProblem(errors);
            }

            var now = timeProvider.GetUtcNow();
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
            cart.MarkConverted(now);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The other of two concurrent checkouts of this very cart
                // committed first: it deleted the same cart rows this
                // deletion expected to find. The transaction rolls back
                // automatically when it is disposed uncommitted, and the
                // answer is the same as when the empty-cart check above
                // caught the loser — the cart is already consumed.
                return Results.NotFound();
            }

            await transaction.CommitAsync(ct);

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
