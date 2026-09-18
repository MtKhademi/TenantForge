using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Features.Payments;

internal sealed record PaymentInitiation(string GatewayReference, string RedirectUrl);

internal sealed record PaymentVerification(bool Succeeded);

internal interface IShopPaymentGateway
{
    Task<PaymentInitiation> InitiateAsync(ShopOrder order, CancellationToken ct);

    Task<PaymentVerification> VerifyCallbackAsync(string gatewayReference, bool approved, CancellationToken ct);
}
