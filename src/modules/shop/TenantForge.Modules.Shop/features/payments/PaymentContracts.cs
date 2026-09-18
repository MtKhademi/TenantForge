namespace TenantForge.Modules.Shop.Features.Payments;

public sealed record InitiatePaymentResponse(string GatewayReference, string RedirectUrl);

public sealed record PaymentCallbackRequest(string? GatewayReference, bool Approved);

public sealed record PaymentCallbackResponse(string OrderId, string Status);
