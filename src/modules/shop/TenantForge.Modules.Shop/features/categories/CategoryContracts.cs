namespace TenantForge.Modules.Shop.Features.Categories;

public sealed record CreateCategoryRequest(string? Name, string? Slug, int DisplayOrder, string? ParentCategoryId);

public sealed record UpdateCategoryRequest(string? Name, string? Slug, int DisplayOrder, bool IsActive, string? ParentCategoryId);

public sealed record CategoryResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive,
    string? ParentCategoryId);

public sealed record CategoryListResponse(
    IReadOnlyList<CategoryResponse> Categories,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
