using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    // B041: RedemptionLimit bounds, enforced in validation code (the Shop module
    // uses no database check constraints — see the B041 Spec's "where the module
    // already uses database check constraints" clause, which does not apply here).
    private const int MinRedemptionLimit = 1;
    private const int MaxRedemptionLimit = 1_000_000;

    public static IEndpointRouteBuilder MapCouponsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/coupons", async (
            string tenantId,
            CreateCouponRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.ShippingManagePermission);
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

            if (request.MinimumSubtotal < 0)
            {
                errors["minimumSubtotal"] = ["Minimum subtotal must be zero or greater."];
            }

            if (request.MaximumDiscountAmount is { } maximum && maximum < 0)
            {
                errors["maximumDiscountAmount"] = ["Maximum discount amount must be zero or greater."];
            }

            AddRedemptionLimitError(errors, request.RedemptionLimit);

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var normalizedCode = request.Code!.Trim().ToUpperInvariant();
            var duplicate = await db.Coupons.AnyAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.NormalizedCode == normalizedCode);
            if (duplicate) return DuplicateCouponProblem();

            var coupon = ShopCoupon.Create(
                access.TenantId, request.Code!, discountType, request.DiscountValue,
                request.MinimumSubtotal, request.MaximumDiscountAmount, request.RedemptionLimit, request.ExpiresAtUtc);
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

        endpoints.MapPut("/api/tenants/{tenantId}/shop/coupons/{couponId}", async (
            string tenantId,
            string couponId,
            UpdateCouponRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.ShippingManagePermission);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(couponId, out var couponTsid)) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            if (request.DiscountValue <= 0)
            {
                errors["discountValue"] = ["Discount value must be positive."];
            }

            if (request.MinimumSubtotal < 0)
            {
                errors["minimumSubtotal"] = ["Minimum subtotal must be zero or greater."];
            }

            if (request.MaximumDiscountAmount is { } maximum && maximum < 0)
            {
                errors["maximumDiscountAmount"] = ["Maximum discount amount must be zero or greater."];
            }

            AddRedemptionLimitError(errors, request.RedemptionLimit);

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var coupon = await db.Coupons.SingleOrDefaultAsync(coupon =>
                coupon.TenantId == access.TenantId && coupon.Id == couponTsid);
            if (coupon is null) return Results.NotFound();

            // Spec step 10c: the normalized code and the discount type are
            // immutable after the first redemption. UpdateCouponRequest carries
            // neither field, so both are structurally immutable through this
            // endpoint — no check is needed. The two rules that DO read the
            // loaded row are the limit-floor (10b) and the version guard (10a).
            if (request.RedemptionLimit is { } newLimit && newLimit < coupon.RedeemedCount)
            {
                errors["redemptionLimit"] = ["Redemption limit cannot be set below the current redeemed count."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (coupon.Version != request.ExpectedVersion)
            {
                return StaleVersionProblem();
            }

            coupon.Update(
                request.DiscountValue, request.MinimumSubtotal, request.MaximumDiscountAmount,
                request.RedemptionLimit, request.ExpiresAtUtc, request.IsActive);
            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(coupon));
        }).RequireAuthorization();

        endpoints.            MapPatch("/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", async (
            string tenantId,
            string couponId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.ShippingManagePermission);
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

    /// <summary>
    /// B041: null is "unlimited" (always valid); a set value must be within
    /// 1..1,000,000 inclusive. Adds a field error to <paramref name="errors"/>
    /// when invalid.
    /// </summary>
    private static void AddRedemptionLimitError(Dictionary<string, string[]> errors, int? redemptionLimit)
    {
        if (redemptionLimit is { } limit && (limit < MinRedemptionLimit || limit > MaxRedemptionLimit))
        {
            errors["redemptionLimit"] = ["Redemption limit must be between 1 and 1000000."];
        }
    }

    private static IResult DuplicateCouponProblem() => Results.Problem(
        title: "Duplicate coupon code",
        detail: "A coupon with this code already exists in this tenant.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult StaleVersionProblem() => Results.Problem(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Type = "stale_version",
        Title = "Coupon version conflict",
        Detail = "The coupon was changed before this request was applied. Reload and try again."
    });

    private static CouponResponse ToResponse(ShopCoupon coupon) => new(
        TsidId.Format(coupon.Id),
        coupon.Code,
        coupon.DiscountType.ToString(),
        coupon.DiscountValue,
        coupon.MinimumSubtotal,
        coupon.MaximumDiscountAmount,
        coupon.RedemptionLimit,
        coupon.RedeemedCount,
        coupon.IsActive,
        coupon.ExpiresAtUtc,
        coupon.Version);
}
