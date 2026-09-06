using System.Security.Cryptography;
using System.Text;

namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantInvitation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private TenantInvitation()
    {
    }

    public static TenantInvitation Create(Guid tenantId, string email, string role, string rawToken, DateTimeOffset nowUtc)
    {
        var normalizedEmail = NormalizeEmail(email);
        var now = nowUtc.ToUniversalTime();
        return new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = normalizedEmail,
            NormalizedEmail = normalizedEmail,
            Role = role.Trim(),
            Status = "Pending",
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
