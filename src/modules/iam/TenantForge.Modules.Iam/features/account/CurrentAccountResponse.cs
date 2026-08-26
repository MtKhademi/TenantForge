namespace TenantForge.Modules.Iam.Features.Account;

public record CurrentAccountResponse(
    string Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin);
