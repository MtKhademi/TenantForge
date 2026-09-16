namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantMemberResponse(
    string Id,
    string UserId,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAtUtc);
