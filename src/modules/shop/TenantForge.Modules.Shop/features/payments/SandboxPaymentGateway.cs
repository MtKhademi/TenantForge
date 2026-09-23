using System.Security.Cryptography;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Features.Payments;

/// <summary>
/// B044: the sandbox's <see cref="IShopPaymentGateway"/>. It never makes an
/// outbound HTTP call to any external service and never touches the database —
/// it is a pure decision function. "Initiating" produces an authority (a
/// cryptographically random 32-char hex string, stored in the attempt's
/// <c>GatewayReference</c>) plus the redirect URL to the in-app fake bank page
/// (<c>/shop/{tenantId}/bank?authority=…</c>, the real frontend route the
/// customer approves/declines on). "Verifying" maps that page's
/// <c>approved</c> callback value to a <see cref="GatewayVerification"/>; the
/// attempt/order state change then runs exclusively through
/// <see cref="ShopPaymentCompletionService"/>, which this class never calls.
/// See docs/design/shop/payments.md for the trust boundary this sits under.
/// </summary>
internal sealed class SandboxPaymentGateway : IShopPaymentGateway
{
    public const string ProviderName = "Sandbox";

    public string Provider => ProviderName;

    public Task<GatewayInitiation> InitiateAsync(PaymentContext context, CancellationToken ct)
    {
        // The sandbox authority doubles as its gateway reference. 16 random
        // bytes → 32 lowercase hex chars, well inside the 60-char column.
        var authority = GenerateAuthority();
        var redirect = new Uri($"/shop/{TsidId.Format(context.TenantId)}/bank?authority={authority}", UriKind.Relative);
        return Task.FromResult(new GatewayInitiation(authority, redirect));
    }

    public Task<GatewayVerification> VerifyAsync(PaymentVerificationRequest request, CancellationToken ct)
    {
        // The fake bank page hands back a single "approved" flag. This value
        // is the only browser-authored input the whole payment flow accepts,
        // and only because it is a Development-only simulation of a provider
        // page — a real provider verifies server-to-server instead (B045).
        // The sandbox is an in-app simulation with no provider-side
        // transaction record, so it contributes no provider reference.
        if (!request.CallbackValues.TryGetValue("approved", out var raw)
            || !bool.TryParse(raw, out var approved))
        {
            return Task.FromResult(new GatewayVerification(
                GatewayOutcome.Failed, ReferenceId: null, "verification_failed"));
        }

        return Task.FromResult(approved
            ? new GatewayVerification(GatewayOutcome.Succeeded, ReferenceId: null, ErrorCode: null)
            : new GatewayVerification(GatewayOutcome.Failed, ReferenceId: null, "payment_declined"));
    }

    private static string GenerateAuthority()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
