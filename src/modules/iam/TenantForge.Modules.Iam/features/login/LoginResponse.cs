namespace TenantForge.Modules.Iam.Features.Login;

public record LoginResponse(
    string AccessToken,
    string ExpiresAtUtc,
    LoginUserResponse User);

public record LoginUserResponse(
    string Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin);
