namespace TenantForge.Modules.Iam.Features.Login;

public static class DevelopmentAdminIdentity
{
    public const string Id = "development-admin";
    public const string DisplayName = "Platform Administrator";
    public const bool IsPlatformAdmin = true;
    public const string Audience = "TenantForge";
    public const string Issuer = "TenantForge";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
}
