using TenantForge.Modules.Iam.Contract.Queries;

namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record PaginationMetadata(int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PaginationMetadata From(PaginationQuery query, int totalCount)
    {
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new(query.PageNumber, query.PageSize, totalCount, totalPages, query.PageNumber > 1, query.PageNumber < totalPages);
    }
}
