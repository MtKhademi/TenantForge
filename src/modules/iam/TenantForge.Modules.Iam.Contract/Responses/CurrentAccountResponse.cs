namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record CurrentAccountResponse(
    string Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin);
