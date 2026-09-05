namespace TenantForge.Modules.Iam.Domain;

internal sealed class Tenant
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string NormalizedSlug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; } = TenantStatus.Active;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private Tenant()
    {
    }

    public static Tenant Create(string name, string slug, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name is required.", nameof(name));
        }

        var normalizedSlug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            throw new ArgumentException("Tenant slug is required.", nameof(slug));
        }

        var now = nowUtc.ToUniversalTime();
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = normalizedSlug,
            NormalizedSlug = normalizedSlug,
            Status = TenantStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public static string NormalizeSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var characters = new List<char>(normalized.Length);
        var previousWasDash = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || character == '_')
            {
                AppendDash(characters, ref previousWasDash);
                continue;
            }

            if (character == '-')
            {
                AppendDash(characters, ref previousWasDash);
                continue;
            }

            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                characters.Add(character);
                previousWasDash = false;
            }
        }

        while (characters.Count > 0 && characters[0] == '-')
        {
            characters.RemoveAt(0);
        }

        while (characters.Count > 0 && characters[^1] == '-')
        {
            characters.RemoveAt(characters.Count - 1);
        }

        return new string(characters.ToArray());
    }

    private static void AppendDash(List<char> characters, ref bool previousWasDash)
    {
        if (characters.Count == 0 || previousWasDash)
        {
            return;
        }

        characters.Add('-');
        previousWasDash = true;
    }
}
