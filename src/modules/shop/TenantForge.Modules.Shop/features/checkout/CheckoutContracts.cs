namespace TenantForge.Modules.Shop.Features.Checkout;

public sealed record CheckoutSummaryRequest(
    string? CartId,
    string? ShippingProvince,
    string? ShippingCity,
    string? ShippingAddressLine,
    string? ShippingPostalCode,
    string? CouponCode);

public sealed record CheckoutSummaryResponse(
    decimal SubTotal,
    decimal DiscountAmount,
    decimal ShippingCost,
    decimal GrandTotal);
