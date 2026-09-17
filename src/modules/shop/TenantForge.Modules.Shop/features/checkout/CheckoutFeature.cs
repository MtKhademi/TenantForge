using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Checkout;

internal static class CheckoutFeature
{
    public static IEndpointRouteBuilder MapCheckoutFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/checkout/summary", async (
            string tenantId,
            CheckoutSummaryRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.CartId, out var cartTsid)) return Results.NotFound();

            var cartExists = await db.Carts.AnyAsync(cart => cart.Id == cartTsid && cart.TenantId == tenantTsid);
            if (!cartExists) return Results.NotFound();

            var subTotal = await db.CartItems.AsNoTracking()
                .Where(item => item.CartId == cartTsid)
                .SumAsync(item => item.UnitPriceSnapshot * item.Quantity);

            if (subTotal <= 0) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

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

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var grandTotal = subTotal - discountAmount + shippingCost;
            return Results.Ok(new CheckoutSummaryResponse(subTotal, discountAmount, shippingCost, grandTotal));
        });

        return endpoints;
    }
}
