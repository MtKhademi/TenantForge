namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// The identity of an account that passed database-backed credential
/// verification. This is the only identity the login endpoint may mint tokens
/// from: no field comes from the request.
/// </summary>
public sealed record AuthenticatedAccount(
    string Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin);
