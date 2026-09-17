namespace TenantForge.Modules.Shop.Features.Shipping;

public sealed record SetShippingRateRequest(string? ProvinceName, decimal Cost);

public sealed record ShippingRateResponse(string Id, string ProvinceName, decimal Cost);

public sealed record ShippingRateListResponse(IReadOnlyList<ShippingRateResponse> Rates);
