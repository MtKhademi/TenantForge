using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace TenantForge.Modules.Iam.Features.Pagination;

internal sealed record PaginationQuery(int PageNumber, int PageSize)
{
    public int Offset => checked((PageNumber - 1) * PageSize);
}

internal sealed record PaginationMetadata(int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PaginationMetadata From(PaginationQuery query, int totalCount)
    {
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new(query.PageNumber, query.PageSize, totalCount, totalPages, query.PageNumber > 1, query.PageNumber < totalPages);
    }
}

internal static class PaginationSupport
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static bool TryBind(HttpRequest request, out PaginationQuery query, out Dictionary<string, string[]> errors)
    {
        errors = new(StringComparer.OrdinalIgnoreCase);
        var pageNumber = Parse(request, "pageNumber", DefaultPageNumber, int.MaxValue, errors);
        var pageSize = Parse(request, "pageSize", DefaultPageSize, MaxPageSize, errors);

        if (errors.Count > 0)
        {
            query = new(DefaultPageNumber, DefaultPageSize);
            return false;
        }

        try
        {
            _ = checked((pageNumber - 1) * pageSize);
        }
        catch (OverflowException)
        {
            errors["pageNumber"] = ["The requested page is too large."];
            query = new(DefaultPageNumber, DefaultPageSize);
            return false;
        }

        query = new(pageNumber, pageSize);
        return true;
    }

    public static async Task<(IReadOnlyList<T> Items, PaginationMetadata Pagination)> PageAsync<T>(IQueryable<T> queryable, PaginationQuery query)
    {
        var totalCount = await queryable.CountAsync();
        var items = await queryable.Skip(query.Offset).Take(query.PageSize).ToListAsync();
        return (items, PaginationMetadata.From(query, totalCount));
    }

    private static int Parse(HttpRequest request, string field, int defaultValue, int maxValue, Dictionary<string, string[]> errors)
    {
        if (!request.Query.TryGetValue(field, out var values)) return defaultValue;
        var value = values.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{field} must be a number."];
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 1 || parsed > maxValue)
        {
            errors[field] = field == "pageSize"
                ? [$"pageSize must be between 1 and {MaxPageSize}."]
                : ["pageNumber must be 1 or greater."];
            return defaultValue;
        }

        return parsed;
    }
}
