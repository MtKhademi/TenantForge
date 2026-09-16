namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantListResponse(IReadOnlyList<TenantSummaryResponse> Tenants, PaginationMetadata Pagination);
