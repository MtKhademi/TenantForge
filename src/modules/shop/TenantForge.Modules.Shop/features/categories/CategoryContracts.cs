namespace TenantForge.Modules.Shop.Features.Categories;

public sealed record CreateCategoryRequest(string? Name, string? Slug, int DisplayOrder);

public sealed record UpdateCategoryRequest(string? Name, string? Slug, int DisplayOrder, bool IsActive);

public sealed record CategoryResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive);

public sealed record CategoryListResponse(
    IReadOnlyList<CategoryResponse> Categories,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
