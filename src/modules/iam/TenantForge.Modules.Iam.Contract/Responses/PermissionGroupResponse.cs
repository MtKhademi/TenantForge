namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record PermissionGroupResponse(string Id, string Label, string Description, IReadOnlyList<PermissionResponse> Permissions);
