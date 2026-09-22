namespace TenantForge.Modules.Shop.Features.Carts;

public sealed record CreateCartResponse(string CartId, DateTimeOffset ExpiresAtUtc);

public sealed record AddCartItemRequest(string? ProductVariantId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartItemResponse(
    string Id,
    string ProductVariantId,
    string ProductName,
    string VariantLabel,
    int Quantity,
    decimal UnitPrice);

public sealed record CartResponse(
    string CartId,
    IReadOnlyList<CartItemResponse> Items,
    decimal SubTotal,
    DateTimeOffset ExpiresAtUtc);
