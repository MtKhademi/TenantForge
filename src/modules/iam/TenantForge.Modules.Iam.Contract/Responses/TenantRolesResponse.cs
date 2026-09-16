namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantRolesResponse(IReadOnlyList<TenantRoleResponse> Roles);
