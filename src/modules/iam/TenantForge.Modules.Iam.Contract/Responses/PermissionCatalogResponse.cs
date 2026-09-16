namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionGroupResponse> Groups);
