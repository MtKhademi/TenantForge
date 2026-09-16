namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record ResolvedPermissionsResponse(IReadOnlyList<string> Permissions);
