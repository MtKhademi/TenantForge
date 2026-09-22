using TenantForge.Modules.Shop.Features.Media;

namespace TenantForge.Modules.Shop.Features.Storefront;

public sealed record StorefrontCategoryResponse(
    string Id, string Name, string Slug, int DisplayOrder,
    IReadOnlyList<StorefrontCategoryResponse> Children);

public sealed record StorefrontCategoryListResponse(IReadOnlyList<StorefrontCategoryResponse> Categories);

public sealed record StorefrontProductSummaryResponse(
    string Id,
    string Name,
    string Slug,
    decimal EffectivePrice,
    decimal? CompareAtPrice,
    bool IsOnSale,
    bool IsSoldOut,
    string? ThumbnailUrl,
    IReadOnlyList<ProductImageResponse> Images);

public sealed record StorefrontProductListResponse(
    IReadOnlyList<StorefrontProductSummaryResponse> Products,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);

public sealed record StorefrontVariantResponse(
    string Id,
    string Color,
    string Size,
    int StockQuantity,
    decimal EffectivePrice);

public sealed record StorefrontSizeGuideColumnResponse(string Id, string Name, int DisplayOrder);

public sealed record StorefrontSizeGuideCellResponse(string ColumnId, string Value);

public sealed record StorefrontSizeGuideRowResponse(
    string SizeLabel,
    int DisplayOrder,
    IReadOnlyList<StorefrontSizeGuideCellResponse> Cells);

public sealed record StorefrontProductDetailResponse(
    string Id,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    IReadOnlyList<ProductImageResponse> Images,
    IReadOnlyList<StorefrontVariantResponse> Variants,
    IReadOnlyList<StorefrontSizeGuideColumnResponse> SizeGuideColumns,
    IReadOnlyList<StorefrontSizeGuideRowResponse> SizeGuideRows);
