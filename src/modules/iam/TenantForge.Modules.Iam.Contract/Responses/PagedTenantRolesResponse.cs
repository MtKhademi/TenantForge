namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record PagedTenantRolesResponse(IReadOnlyList<TenantRoleResponse> Roles, PaginationMetadata Pagination);
