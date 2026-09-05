namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Bound from the <c>IAM:Auth</c> section. Holds only the JWT signing key; the
/// previous "DevelopmentLogin" section that mixed email/password with the signing
/// key is gone now that credentials come from the persisted account (B005).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string? SigningKey { get; init; }
}
