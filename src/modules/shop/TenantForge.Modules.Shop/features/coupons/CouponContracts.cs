namespace TenantForge.Modules.Shop.Features.Coupons;

public sealed record CreateCouponRequest(
    string? Code,
    string? DiscountType,
    decimal DiscountValue,
    decimal MinimumSubtotal,
    decimal? MaximumDiscountAmount,
    int? RedemptionLimit,
    DateTimeOffset? ExpiresAtUtc);

public sealed record UpdateCouponRequest(
    decimal DiscountValue,
    decimal MinimumSubtotal,
    decimal? MaximumDiscountAmount,
    int? RedemptionLimit,
    DateTimeOffset? ExpiresAtUtc,
    bool IsActive,
    int ExpectedVersion);

public sealed record CouponResponse(
    string Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    decimal MinimumSubtotal,
    decimal? MaximumDiscountAmount,
    int? RedemptionLimit,
    int RedeemedCount,
    bool IsActive,
    DateTimeOffset? ExpiresAtUtc,
    int Version);

public sealed record CouponListResponse(
    IReadOnlyList<CouponResponse> Coupons,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
