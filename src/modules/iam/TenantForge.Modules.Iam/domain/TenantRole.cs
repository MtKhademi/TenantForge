using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantRole
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Description { get; private set; } = "نقش سفارشی مستأجر؛ قابل ویرایش و قابل انتساب به اعضا.";
    public string Kind { get; private set; } = "custom";
    public List<string> PermissionKeys { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private TenantRole()
    {
    }

    public static TenantRole Create(Tsid tenantId, string name, IEnumerable<string> permissionKeys, DateTimeOffset nowUtc)
    {
        var trimmedName = name.Trim();
        var now = nowUtc.ToUniversalTime();
        return new TenantRole
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Name = trimmedName,
            NormalizedName = NormalizeName(trimmedName),
            PermissionKeys = permissionKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void ReplacePermissions(IEnumerable<string> permissionKeys, DateTimeOffset nowUtc)
    {
        PermissionKeys = permissionKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}
