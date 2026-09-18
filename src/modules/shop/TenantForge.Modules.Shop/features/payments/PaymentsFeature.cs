using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

internal static class PaymentsFeature
{
    public static IEndpointRouteBuilder MapPaymentsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/initiate", async (
            string tenantId,
            string orderId,
            IShopPaymentGateway gateway,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return Results.NotFound();

            var order = await db.Orders.SingleOrDefaultAsync(order => order.Id == orderTsid && order.TenantId == tenantTsid);
            if (order is null) return Results.NotFound();

            if (order.Status != ShopOrderStatus.PendingPayment)
            {
                return Results.Problem(
                    title: "Order is not awaiting payment",
                    detail: $"This order's current status is '{order.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var initiation = await gateway.InitiateAsync(order, CancellationToken.None);
            return Results.Ok(new InitiatePaymentResponse(initiation.GatewayReference, initiation.RedirectUrl));
        });

        endpoints.MapPost("/api/shop/{tenantId}/orders/{orderId}/payments/callback", async (
            string tenantId,
            string orderId,
            PaymentCallbackRequest request,
            IShopPaymentGateway gateway,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();
            if (!TsidId.TryParse(orderId, out var orderTsid)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.GatewayReference))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["gatewayReference"] = ["gatewayReference is required."],
                });
            }

            var order = await db.Orders.SingleOrDefaultAsync(order => order.Id == orderTsid && order.TenantId == tenantTsid);
            if (order is null) return Results.NotFound();

            if (order.Status != ShopOrderStatus.PendingPayment)
            {
                return Results.Problem(
                    title: "Order already resolved",
                    detail: $"This order's current status is '{order.Status}'.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var verification = await gateway.VerifyCallbackAsync(request.GatewayReference, request.Approved, CancellationToken.None);
            if (!verification.Succeeded && !request.Approved)
            {
                // A legitimate decline: the gateway resolved the attempt as
                // Failed. Distinguish this from an unknown/duplicate
                // gatewayReference (also "not succeeded") using the attempt
                // row directly.
                var attemptExists = await db.PaymentAttempts.AnyAsync(a => a.GatewayReference == request.GatewayReference);
                if (!attemptExists)
                {
                    return Results.NotFound();
                }

                order.MarkPaymentFailed();
                await db.SaveChangesAsync();
                return Results.Ok(new PaymentCallbackResponse(TsidId.Format(order.Id), order.Status.ToString()));
            }

            if (!verification.Succeeded)
            {
                // approved=true but the gateway could not resolve the
                // attempt (unknown reference, or already resolved once).
                return Results.Problem(
                    title: "Payment callback rejected",
                    detail: "The payment attempt could not be verified.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            order.MarkPaid();
            await db.SaveChangesAsync();
            return Results.Ok(new PaymentCallbackResponse(TsidId.Format(order.Id), order.Status.ToString()));
        });

        return endpoints;
    }
}
