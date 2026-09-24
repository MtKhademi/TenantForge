using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Features.RateLimiting;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Orders;

internal static class OrderLookupFeature
{
    public static IEndpointRouteBuilder MapOrderLookupFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders/lookup", async (
            string tenantId,
            OrderLookupRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(request.TrackingCode))
            {
                errors["trackingCode"] = ["Tracking code is required."];
            }

            if (string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                errors["customerPhone"] = ["Phone number is required."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var trackingCode = request.TrackingCode!.Trim();
            var customerPhone = request.CustomerPhone!.Trim();

            // Exact match on BOTH values together, in one query — never
            // resolve by trackingCode alone and compare the phone
            // separately, which would make it observable (by response
            // timing or shape) which half was wrong.
            var order = await db.Orders.AsNoTracking()
                .SingleOrDefaultAsync(order =>
                    order.TenantId == tenantTsid
                    && order.TrackingCode == trackingCode
                    && order.CustomerPhone == customerPhone);

            if (order is null)
            {
                return NotFoundProblem();
            }

            var items = await db.OrderItems.AsNoTracking()
                .Where(item => item.OrderId == order.Id)
                .Select(item => new OrderLookupItemResponse(
                    item.ProductNameSnapshot, item.VariantLabelSnapshot, item.UnitPrice, item.Quantity))
                .ToListAsync();

            var response = new OrderLookupResponse(
                order.OrderNumber,
                order.Status.ToString(),
                order.CreatedAtUtc,
                order.ShippingProvince,
                order.ShippingCity,
                order.ShippingAddressLine,
                order.ShippingPostalCode,
                order.SubTotal,
                order.ShippingCost,
                order.DiscountAmount,
                order.GrandTotal,
                items);

            return Results.Ok(response);
        }).RequireRateLimiting(ShopRateLimitPolicies.OrderLookup);

        return endpoints;
    }

    /// <summary>
    /// The one, identical not-found shape for both "tracking code does not
    /// exist" and "tracking code exists, phone does not match" — reused
    /// exactly, never rebuilt inline, so the two cases can never drift into
    /// two different bodies by accident.
    /// </summary>
    private static IResult NotFoundProblem() => Results.Problem(
        title: "Order not found",
        detail: "No order was found for this tracking code and phone number.",
        statusCode: StatusCodes.Status404NotFound);
}
