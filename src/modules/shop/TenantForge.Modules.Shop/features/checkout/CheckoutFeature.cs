using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Features.Coupons;
using TenantForge.Modules.Shop.Features.RateLimiting;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Checkout;

internal static class CheckoutFeature
{
    public static IEndpointRouteBuilder MapCheckoutFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/checkout/summary", async (
            string tenantId,
            CheckoutSummaryRequest request,
            ShopDbContext db,
            IShopCartExpiryService expiryService,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(request.CartId, out var cartTsid)) return Results.NotFound();

            var lease = await expiryService.EnsureActiveAsync(tenantTsid, cartTsid, ct);
            if (lease.Cart is null) return Results.NotFound();
            if (CartsFeature.IsExpired(lease)) return CartsFeature.CartExpiredProblem();
            if (lease.Outcome == CartLeaseOutcome.Converted) return Results.NotFound();

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
                // B041: preview mode — Evaluate is pure and never writes. The
                // lookup is tenant-first, so a null result is coupon_not_found,
                // identical for "does not exist" and "belongs to another tenant"
                // (nothing is leaked either way).
                var normalizedCode = request.CouponCode.Trim().ToUpperInvariant();
                var coupon = await db.Coupons.AsNoTracking()
                    .SingleOrDefaultAsync(coupon => coupon.TenantId == tenantTsid && coupon.NormalizedCode == normalizedCode);

                if (coupon is null)
                {
                    errors["couponCode"] = [ShopCouponPolicy.MessageFor(ShopCouponPolicy.CouponNotFound)];
                }
                else
                {
                    var evaluation = ShopCouponPolicy.Evaluate(coupon, subTotal, timeProvider.GetUtcNow());
                    if (!evaluation.IsValid)
                    {
                        errors["couponCode"] = [ShopCouponPolicy.MessageFor(evaluation.ErrorCode!)];
                    }
                    else
                    {
                        discountAmount = evaluation.DiscountAmount;
                    }
                }
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var grandTotal = subTotal - discountAmount + shippingCost;
            return Results.Ok(new CheckoutSummaryResponse(subTotal, discountAmount, shippingCost, grandTotal));
        }).RequireRateLimiting(ShopRateLimitPolicies.CheckoutOrder);

        return endpoints;
    }
}
