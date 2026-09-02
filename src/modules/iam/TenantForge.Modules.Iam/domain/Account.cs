namespace TenantForge.Modules.Iam.Domain;

internal sealed class Account
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsPlatformAdmin { get; private set; }
    public AccountStatus Status { get; private set; } = AccountStatus.Active;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private Account()
    {
    }

    public static Account CreatePlatformAdmin(
        string email,
        string displayName,
        string passwordHash,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        var trimmedEmail = email.Trim();
        var trimmedDisplayName = displayName.Trim();
        var trimmedPasswordHash = passwordHash.Trim();

        return new Account
        {
            Id = Guid.NewGuid(),
            Email = trimmedEmail,
            NormalizedEmail = NormalizeEmail(trimmedEmail),
            DisplayName = trimmedDisplayName,
            PasswordHash = trimmedPasswordHash,
            IsPlatformAdmin = true,
            Status = AccountStatus.Active,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
