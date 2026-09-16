namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record UpdateRoleRequest(IReadOnlyList<string>? PermissionKeys);
