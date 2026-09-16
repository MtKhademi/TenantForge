namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record UserResponse(
    string Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsPlatformAdmin,
    string CreatedAtUtc);
