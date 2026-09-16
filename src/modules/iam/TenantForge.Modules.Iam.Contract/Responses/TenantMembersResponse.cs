namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantMembersResponse(
    TenantContextResponse Tenant,
    IReadOnlyList<TenantMemberResponse> Members,
    PaginationMetadata Pagination);
