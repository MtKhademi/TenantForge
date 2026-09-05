namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Static JWT parameters that are not tied to any particular account: issuer,
/// audience and token lifetime. The account-specific identity (id, email,
/// display name, admin flag) now comes from the persisted <c>Account</c>, not
/// from a hardcoded development identity.
/// </summary>
public static class JwtConstants
{
    public const string Issuer = "TenantForge";
    public const string Audience = "TenantForge";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
}
