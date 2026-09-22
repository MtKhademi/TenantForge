namespace TenantForge.Modules.Shop.Features.Profiles;

/// <summary>
/// B039: the admin's full save payload for the tenant's storefront profile.
/// <see cref="ExpectedVersion"/> is null on the first save (create) and the
/// profile's current version on every update — a stale value is a 409, never a
/// silent overwrite. <see cref="TenantId"/> is deliberately absent: the server
/// always derives the tenant from the authenticated route context.
/// </summary>
public sealed record SaveShopProfileRequest(
    string? Name, string? Tagline, string? SupportPhone, string? InstagramUrl,
    string? AboutText, string? ShippingPolicy, string? PaymentPolicy,
    string? ReturnPolicy, string? PrivacyPolicy, bool IsPublished, int? ExpectedVersion);

public sealed record ShopProfileDto(
    string Id, string TenantId, string Name, string Tagline,
    string SupportPhone, string? InstagramUrl, string AboutText,
    string ShippingPolicy, string PaymentPolicy, string ReturnPolicy,
    string PrivacyPolicy, bool IsPublished, int Version,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// B039: the admin read envelope — <c>Profile: null</c> is the valid
/// "no profile yet" state (a 200, not a 404).
/// </summary>
public sealed record ShopProfileResponse(ShopProfileDto? Profile);

/// <summary>
/// B039: the anonymous, public-facing profile. Deliberately has no Id,
/// TenantId, Version or UpdatedAtUtc — the internal concurrency counter and
/// ids are never part of the public wire shape.
/// </summary>
public sealed record PublicShopProfileResponse(
    string Name, string Tagline, string SupportPhone, string? InstagramUrl,
    string AboutText, string ShippingPolicy, string PaymentPolicy,
    string ReturnPolicy, string PrivacyPolicy, bool IsPublished);
