namespace TenantForge.Modules.Iam.Features.Login;

public sealed class DevelopmentLoginOptions
{
    public const string SectionName = "DevelopmentLogin";
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? SigningKey { get; init; }
}
