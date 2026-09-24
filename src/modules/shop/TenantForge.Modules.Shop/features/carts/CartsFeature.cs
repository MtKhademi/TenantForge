using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Shop;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.RateLimiting;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Carts;

internal static class CartsFeature
{
    public static IEndpointRouteBuilder MapCartsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/carts", async (
            string tenantId,
            ShopDbContext db,
            TimeProvider timeProvider,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var now = timeProvider.GetUtcNow();
            var leaseDuration = TimeSpan.FromMinutes(ShopConfig.GetCartReservationMinutes(environment, configuration));
            var cart = ShopCart.Create(tenantTsid, now, now.Add(leaseDuration));
            db.Carts.Add(cart);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/shop/{tenantId}/carts/{TsidId.Format(cart.Id)}", new CreateCartResponse(TsidId.Format(cart.Id), cart.ExpiresAtUtc));
        }).RequireRateLimiting(ShopRateLimitPolicies.CartMutation);

        endpoints.MapPost("/api/shop/{tenantId}/carts/{cartId}/items", async (
            string tenantId,
            string cartId,
            AddCartItemRequest request,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            TimeProvider timeProvider,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken ct) =>
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

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (IsExpired(lease)) return CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

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

            lease.Cart.ExtendLease(timeProvider.GetUtcNow(), TimeSpan.FromMinutes(ShopConfig.GetCartReservationMinutes(environment, configuration)));
            await db.SaveChangesAsync(ct);

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid, ct));
        }).RequireRateLimiting(ShopRateLimitPolicies.CartMutation);

        endpoints.MapPatch("/api/shop/{tenantId}/carts/{cartId}/items/{itemId}", async (
            string tenantId,
            string cartId,
            string itemId,
            UpdateCartItemRequest request,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            TimeProvider timeProvider,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken ct) =>
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

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (IsExpired(lease)) return CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

            var item = await db.CartItems.SingleOrDefaultAsync(item => item.Id == itemTsid && item.CartId == cartTsid, ct);
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
            lease.Cart.ExtendLease(timeProvider.GetUtcNow(), TimeSpan.FromMinutes(ShopConfig.GetCartReservationMinutes(environment, configuration)));
            await db.SaveChangesAsync(ct);

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid, ct));
        }).RequireRateLimiting(ShopRateLimitPolicies.CartMutation);

        endpoints.MapDelete("/api/shop/{tenantId}/carts/{cartId}/items/{itemId}", async (
            string tenantId,
            string cartId,
            string itemId,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            TimeProvider timeProvider,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid) || !TsidId.TryParse(itemId, out var itemTsid))
            {
                return Results.NotFound();
            }

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (IsExpired(lease)) return CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

            var item = await db.CartItems.SingleOrDefaultAsync(item => item.Id == itemTsid && item.CartId == cartTsid, ct);
            if (item is null) return Results.NotFound();

            // Release the reservation before removing the row.
            await db.ProductVariants
                .Where(v => v.Id == item.ProductVariantId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.StockQuantity, v => v.StockQuantity + item.Quantity));

            db.CartItems.Remove(item);
            lease.Cart.ExtendLease(timeProvider.GetUtcNow(), TimeSpan.FromMinutes(ShopConfig.GetCartReservationMinutes(environment, configuration)));
            await db.SaveChangesAsync(ct);

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid, ct));
        }).RequireRateLimiting(ShopRateLimitPolicies.CartMutation);

        endpoints.MapGet("/api/shop/{tenantId}/carts/{cartId}", async (
            string tenantId,
            string cartId,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(cartId, out var cartTsid)) return Results.NotFound();

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (IsExpired(lease)) return CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

            return Results.Ok(await BuildCartResponseAsync(db, tenantTsid, cartTsid, ct));
        });

        return endpoints;
    }

    private static async Task<CartResponse> BuildCartResponseAsync(ShopDbContext db, Tsid tenantId, Tsid cartId, CancellationToken ct)
    {
        var expiresAtUtc = await db.Carts.AsNoTracking()
            .Where(cart => cart.Id == cartId && cart.TenantId == tenantId)
            .Select(cart => cart.ExpiresAtUtc)
            .SingleAsync(ct);

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
            .ToListAsync(ct);

        var subTotal = rows.Sum(row => row.UnitPrice * row.Quantity);
        return new CartResponse(TsidId.Format(cartId), rows, subTotal, expiresAtUtc);
    }

    internal static bool IsExpired(CartLeaseResult lease) =>
        lease.Outcome is CartLeaseOutcome.JustExpired or CartLeaseOutcome.AlreadyExpired;

    internal static IResult CartExpiredProblem() => Results.Problem(
        type: "shop_cart_expired",
        title: "Cart expired",
        detail: "The cart reservation has expired. Start a new cart before continuing.",
        statusCode: StatusCodes.Status410Gone);

    private static IResult InsufficientStockProblem() => Results.Problem(
        title: "Insufficient stock",
        detail: "The requested quantity is no longer available for this variant.",
        statusCode: StatusCodes.Status409Conflict);
}
