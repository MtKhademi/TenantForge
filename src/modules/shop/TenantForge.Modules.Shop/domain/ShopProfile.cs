using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

/// <summary>
/// B039: a tenant's public storefront identity and policy pages — exactly one
/// row per tenant (enforced by a unique <c>TenantId</c> index, not here).
/// Holds plain text only; never rendered as HTML. <see cref="Version"/> is a
/// client-managed optimistic-concurrency counter (not a database rowversion),
/// starting at 1 on create and incremented on every save.
/// </summary>
internal sealed class ShopProfile
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Tagline { get; private set; } = string.Empty;
    public string SupportPhone { get; private set; } = string.Empty;
    public string? InstagramUrl { get; private set; }
    public string AboutText { get; private set; } = string.Empty;
    public string ShippingPolicy { get; private set; } = string.Empty;
    public string PaymentPolicy { get; private set; } = string.Empty;
    public string ReturnPolicy { get; private set; } = string.Empty;
    public string PrivacyPolicy { get; private set; } = string.Empty;
    public bool IsPublished { get; private set; }
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private ShopProfile()
    {
    }

    /// <summary>
    /// B039: every text field is trimmed on the outside only (internal
    /// newlines preserved); an empty <c>instagramUrl</c> normalizes to
    /// <c>null</c>. All trimming/character/URL validation has already run in the
    /// feature before this factory is called — this is a plain state holder.
    /// </summary>
    public static ShopProfile Create(
        Tsid tenantId,
        string name,
        string tagline,
        string supportPhone,
        string? instagramUrl,
        string aboutText,
        string shippingPolicy,
        string paymentPolicy,
        string returnPolicy,
        string privacyPolicy,
        bool isPublished,
        DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ShopProfile
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Name = name.Trim(),
            Tagline = tagline.Trim(),
            SupportPhone = supportPhone.Trim(),
            InstagramUrl = string.IsNullOrWhiteSpace(instagramUrl) ? null : instagramUrl!.Trim(),
            AboutText = aboutText.Trim(),
            ShippingPolicy = shippingPolicy.Trim(),
            PaymentPolicy = paymentPolicy.Trim(),
            ReturnPolicy = returnPolicy.Trim(),
            PrivacyPolicy = privacyPolicy.Trim(),
            IsPublished = isPublished,
            Version = 1,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    /// <summary>
    /// B039: applies a full replacement of every editable field and bumps
    /// <see cref="Version"/> exactly once. Same outside-only trim rule as
    /// <see cref="Create"/>.
    /// </summary>
    public void Update(
        string name,
        string tagline,
        string supportPhone,
        string? instagramUrl,
        string aboutText,
        string shippingPolicy,
        string paymentPolicy,
        string returnPolicy,
        string privacyPolicy,
        bool isPublished,
        DateTimeOffset nowUtc)
    {
        Name = name.Trim();
        Tagline = tagline.Trim();
        SupportPhone = supportPhone.Trim();
        InstagramUrl = string.IsNullOrWhiteSpace(instagramUrl) ? null : instagramUrl!.Trim();
        AboutText = aboutText.Trim();
        ShippingPolicy = shippingPolicy.Trim();
        PaymentPolicy = paymentPolicy.Trim();
        ReturnPolicy = returnPolicy.Trim();
        PrivacyPolicy = privacyPolicy.Trim();
        IsPublished = isPublished;
        Version++;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }
}
