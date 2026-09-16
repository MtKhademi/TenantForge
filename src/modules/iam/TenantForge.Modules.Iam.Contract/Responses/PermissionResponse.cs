namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record PermissionResponse(string Key, string Label, string Description, string Kind);
