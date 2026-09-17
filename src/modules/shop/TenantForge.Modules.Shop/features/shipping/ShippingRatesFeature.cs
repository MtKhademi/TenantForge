using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Shipping;

internal static class ShippingRatesFeature
{
    public static IEndpointRouteBuilder MapShippingRatesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/shop/shipping-rates", async (
            string tenantId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var rates = await db.ShippingRates.AsNoTracking()
                .Where(rate => rate.TenantId == access.TenantId)
                .OrderBy(rate => rate.ProvinceName)
                .Select(rate => new ShippingRateResponse(TsidId.Format(rate.Id), rate.ProvinceName, rate.Cost))
                .ToListAsync();

            return Results.Ok(new ShippingRateListResponse(rates));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/shop/shipping-rates", async (
            string tenantId,
            SetShippingRateRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (string.IsNullOrWhiteSpace(request.ProvinceName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["provinceName"] = ["Province name is required."],
                });
            }

            var provinceName = request.ProvinceName.Trim();
            var existing = await db.ShippingRates.SingleOrDefaultAsync(rate =>
                rate.TenantId == access.TenantId && rate.ProvinceName == provinceName);

            if (existing is null)
            {
                var rate = ShopShippingRate.Create(access.TenantId, provinceName, request.Cost);
                db.ShippingRates.Add(rate);
                await db.SaveChangesAsync();
                return Results.Ok(new ShippingRateResponse(TsidId.Format(rate.Id), rate.ProvinceName, rate.Cost));
            }

            existing.UpdateCost(request.Cost);
            await db.SaveChangesAsync();
            return Results.Ok(new ShippingRateResponse(TsidId.Format(existing.Id), existing.ProvinceName, existing.Cost));
        }).RequireAuthorization();

        return endpoints;
    }
}
