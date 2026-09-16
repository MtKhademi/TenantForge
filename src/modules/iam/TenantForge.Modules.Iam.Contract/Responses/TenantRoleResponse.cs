namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantRoleResponse(
    string Id,
    string Name,
    string Description,
    string Kind,
    IReadOnlyList<string> PermissionKeys,
    IReadOnlyList<string> MemberIds,
    string CreatedAtUtc,
    string UpdatedAtUtc);
