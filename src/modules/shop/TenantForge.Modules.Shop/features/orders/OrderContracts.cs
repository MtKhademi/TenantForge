namespace TenantForge.Modules.Shop.Features.Orders;

public sealed record CreateOrderRequest(
    string? CartId,
    string? CustomerName,
    string? CustomerPhone,
    string? ShippingProvince,
    string? ShippingCity,
    string? ShippingAddressLine,
    string? ShippingPostalCode,
    string? CouponCode);

public sealed record OrderCreatedResponse(
    string OrderId,
    string OrderNumber,
    string TrackingCode,
    string Status,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal ShippingCost,
    decimal GrandTotal);
