namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record LoginUserResponse(
    string Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin);
