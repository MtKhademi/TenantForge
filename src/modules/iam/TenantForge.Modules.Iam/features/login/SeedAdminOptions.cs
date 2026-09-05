namespace TenantForge.Modules.Iam.Features.Login;

/// <summary>
/// Bound from the <c>IAM:SeedAdmin</c> section. Provides the bootstrap identity
/// for the single platform administrator. This is a one-time bootstrap secret
/// (the initial password); after the account is seeded the password is only
/// ever stored as a hash. All three fields must be present together; a partially
/// present section is treated as a misconfiguration (validated at startup).
/// </summary>
public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? DisplayName { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(DisplayName);
}
