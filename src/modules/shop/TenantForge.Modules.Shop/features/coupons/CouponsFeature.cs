using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Coupons;

internal static class CouponsFeature
{
    public static IEndpointRouteBuilder MapCouponsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/coupons", async (
            string tenantId,
            CreateCouponRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                errors["code"] = ["Coupon code is required."];
            }

            if (!Enum.TryParse<ShopDiscountType>(request.DiscountType, ignoreCase: true, out var discountType))
            {
                errors["discountType"] = ["discountType must be 'Percentage' or 'FixedAmount'."];
            }
            else if (request.DiscountValue <= 0 || (discountType == ShopDiscountType.Percentage && request.DiscountValue > 100))
            {
                errors["discountValue"] = discountType == ShopDiscountType.Percentage
                    ? ["A percentage discount must be between 1 and 100."]
                    : ["Discount value must be positive."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var normalizedCode = request.Code!.Trim().ToUpperInvariant();
            var duplicate = await db.Coupons.AnyAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.NormalizedCode == normalizedCode);
            if (duplicate) return DuplicateCouponProblem();

            var coupon = ShopCoupon.Create(access.TenantId, request.Code!, discountType, request.DiscountValue, request.ExpiresAtUtc);
            db.Coupons.Add(coupon);
            await db.SaveChangesAsync();

            return Results.Created($"/api/tenants/{tenantId}/shop/coupons/{TsidId.Format(coupon.Id)}", ToResponse(coupon));
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/coupons", async (
            string tenantId,
            HttpRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var query = db.Coupons.AsNoTracking()
                .Where(coupon => coupon.TenantId == access.TenantId)
                .OrderBy(coupon => coupon.Code)
                .ThenBy(coupon => coupon.Id);

            var (coupons, pagination) = await PaginationSupport.PageAsync(query, page);
            return Results.Ok(new CouponListResponse(coupons.Select(ToResponse).ToList(), pagination));
        }).RequireAuthorization();

        endpoints.MapPatch("/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", async (
            string tenantId,
            string couponId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(couponId, out var couponTsid)) return Results.NotFound();

            var coupon = await db.Coupons.SingleOrDefaultAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.Id == couponTsid);
            if (coupon is null) return Results.NotFound();

            coupon.Deactivate();
            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(coupon));
        }).RequireAuthorization();

        return endpoints;
    }

    private static IResult DuplicateCouponProblem() => Results.Problem(
        title: "Duplicate coupon code",
        detail: "A coupon with this code already exists in this tenant.",
        statusCode: StatusCodes.Status409Conflict);

    private static CouponResponse ToResponse(ShopCoupon coupon) => new(
        TsidId.Format(coupon.Id),
        coupon.Code,
        coupon.DiscountType.ToString(),
        coupon.DiscountValue,
        coupon.IsActive,
        coupon.ExpiresAtUtc);
}
