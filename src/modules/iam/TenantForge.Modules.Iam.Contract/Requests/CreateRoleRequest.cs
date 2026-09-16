namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record CreateRoleRequest(string? Name, IReadOnlyList<string>? PermissionKeys);
