using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// The one real IShopPaymentGateway implementation in this module. It never
/// makes an outbound HTTP call to any external service — "initiating
/// payment" means minting a ShopPaymentAttempt row and a redirect URL to an
/// in-app frontend route (the fake bank page F038/F039 render), which then
/// calls the callback endpoint directly. See B032's Spec Context and
/// tasks/slices/029-shop-order-and-sandbox-payment.md for the explicit,
/// deliberate scope boundary this class sits inside.
/// </summary>
internal sealed class SandboxPaymentGateway(ShopDbContext db) : IShopPaymentGateway
{
    private const string ProviderName = "Sandbox";

    public async Task<PaymentInitiation> InitiateAsync(ShopOrder order, CancellationToken ct)
    {
        var gatewayReference = GenerateGatewayReference();
        var attempt = ShopPaymentAttempt.Create(order.Id, ProviderName, gatewayReference, DateTimeOffset.UtcNow);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        var redirectUrl = $"/shop/{Format(order.TenantId)}/payments/sandbox/{gatewayReference}";
        return new PaymentInitiation(gatewayReference, redirectUrl);
    }

    public async Task<PaymentVerification> VerifyCallbackAsync(string gatewayReference, bool approved, CancellationToken ct)
    {
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(a => a.GatewayReference == gatewayReference, ct);
        if (attempt is null)
        {
            return new PaymentVerification(false);
        }

        var resolved = attempt.TryResolve(approved, DateTimeOffset.UtcNow);
        if (!resolved)
        {
            // Already resolved once — do not silently re-apply a second
            // callback for the same attempt (B032's Spec Non-goals).
            return new PaymentVerification(false);
        }

        await db.SaveChangesAsync(ct);
        return new PaymentVerification(approved);
    }

    private static string GenerateGatewayReference()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Format(TSID.Creator.NET.Tsid tenantId) => TenantForge.BuildingBlocks.Identifiers.TsidId.Format(tenantId);
}
