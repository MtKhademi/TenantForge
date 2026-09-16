namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantDiscoveryResponse(IReadOnlyList<DiscoveredTenantResponse> Tenants, PaginationMetadata Pagination);
