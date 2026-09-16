namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record LoginResponse(
    string AccessToken,
    string ExpiresAtUtc,
    LoginUserResponse User);
