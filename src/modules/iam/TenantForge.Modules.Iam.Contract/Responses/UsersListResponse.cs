namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record UsersListResponse(IReadOnlyList<UserResponse> Users, PaginationMetadata Pagination);
