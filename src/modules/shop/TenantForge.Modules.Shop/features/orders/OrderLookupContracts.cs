namespace TenantForge.Modules.Shop.Features.Orders;

public sealed record OrderLookupRequest(string? TrackingCode, string? CustomerPhone);

public sealed record OrderLookupItemResponse(string ProductNameSnapshot, string VariantLabelSnapshot, decimal UnitPrice, int Quantity);

public sealed record OrderLookupResponse(
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string ShippingProvince,
    string ShippingCity,
    string ShippingAddressLine,
    string ShippingPostalCode,
    decimal SubTotal,
    decimal ShippingCost,
    decimal DiscountAmount,
    decimal GrandTotal,
    IReadOnlyList<OrderLookupItemResponse> Items);
