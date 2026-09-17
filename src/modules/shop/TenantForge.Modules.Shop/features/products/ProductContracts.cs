namespace TenantForge.Modules.Shop.Features.Products;

public sealed record ProductVariantInput(string? Color, string? Size, string? Sku, int StockQuantity, decimal? PriceOverride);

public sealed record SizeGuideRowInput(string? SizeLabel, IReadOnlyList<string>? Values);

public sealed record CreateProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    string? CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    IReadOnlyList<ProductVariantInput>? Variants,
    IReadOnlyList<string>? SizeGuideColumns,
    IReadOnlyList<SizeGuideRowInput>? SizeGuideRows);

public sealed record UpdateProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    string? CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ProductVariantInput>? Variants,
    IReadOnlyList<string>? SizeGuideColumns,
    IReadOnlyList<SizeGuideRowInput>? SizeGuideRows);

public sealed record ProductVariantResponse(
    string Id,
    string Color,
    string Size,
    string Sku,
    int StockQuantity,
    decimal? PriceOverride);

public sealed record SizeGuideColumnResponse(string Id, string Name, int DisplayOrder);

public sealed record SizeGuideCellResponse(string ColumnId, string Value);

public sealed record SizeGuideRowResponse(
    string Id,
    string SizeLabel,
    int DisplayOrder,
    IReadOnlyList<SizeGuideCellResponse> Cells);

public sealed record ProductResponse(
    string Id,
    string TenantId,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<SizeGuideColumnResponse> SizeGuideColumns,
    IReadOnlyList<SizeGuideRowResponse> SizeGuideRows);

public sealed record ProductSummaryResponse(
    string Id,
    string Name,
    string Slug,
    string CategoryId,
    decimal BasePrice,
    bool IsActive,
    int VariantCount);

public sealed record ProductListResponse(
    IReadOnlyList<ProductSummaryResponse> Products,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
