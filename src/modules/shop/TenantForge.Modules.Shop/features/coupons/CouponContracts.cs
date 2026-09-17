namespace TenantForge.Modules.Shop.Features.Coupons;

public sealed record CreateCouponRequest(string? Code, string? DiscountType, decimal DiscountValue, DateTimeOffset? ExpiresAtUtc);

public sealed record CouponResponse(
    string Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    bool IsActive,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CouponListResponse(
    IReadOnlyList<CouponResponse> Coupons,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
