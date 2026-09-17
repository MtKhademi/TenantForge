namespace TenantForge.Modules.Shop.Features.Pagination;

public sealed record PaginationQuery(int PageNumber, int PageSize)
{
    public int Offset => checked((PageNumber - 1) * PageSize);
}
