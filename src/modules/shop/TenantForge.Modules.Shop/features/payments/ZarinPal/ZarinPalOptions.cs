namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: the bound configuration for the ZarinPal provider, read from the
/// <see cref="SectionPath"/> section (configuration/environment variables only).
///
/// <see cref="MerchantId"/> is a secret: it is never returned in an HTTP
/// response, never logged and never persisted per order. Every URL here is
/// validated at activation (fail closed); outside Development all five must be
/// HTTPS and the three provider-facing hosts must be on the ZarinPal allowlist
/// (see <c>ShopConfig.ValidateZarinPalConfiguration</c>).
/// </summary>
internal sealed class ZarinPalOptions
{
    internal const string SectionPath = "Shop:Payments:ZarinPal";

    /// <summary>The exact, closed set of currencies the provider call accepts.</summary>
    internal static readonly string[] SupportedCurrencies = ["IRR", "IRT"];

    internal const int DefaultTimeoutSeconds = 10;
    internal const int MinTimeoutSeconds = 1;
    internal const int MaxTimeoutSeconds = 300;

    public string MerchantId { get; init; } = string.Empty;

    /// <summary>
    /// Exactly <c>IRR</c> (Rials) or <c>IRT</c> (Tomans). The order amounts
    /// TenantForge stores are Toman values (the current UI's unit), so the
    /// default is <c>IRT</c>; <c>IRR</c> selects the one ×10 conversion.
    /// </summary>
    public string Currency { get; init; } = "IRT";

    /// <summary>The v4 <c>request</c> endpoint (e.g. https://api.zarinpal.com/v4/payment/request).</summary>
    public Uri RequestEndpoint { get; init; } = null!;

    /// <summary>The v4 <c>verify</c> endpoint (e.g. https://api.zarinpal.com/v4/payment/verify).</summary>
    public Uri VerifyEndpoint { get; init; } = null!;

    /// <summary>
    /// The base of the browser redirect target (e.g.
    /// https://checkout.zarinpal.com/payment/). The initiation response's
    /// <c>redirectUrl</c> is this base plus the returned authority — never a
    /// value that carries the merchant id.
    /// </summary>
    public Uri GatewayBaseUrl { get; init; } = null!;

    /// <summary>
    /// The base URL the backend is publicly reachable at — used to build the
    /// server-owned callback URL sent to the provider (NOT the frontend URL).
    /// </summary>
    public Uri PublicApiBaseUrl { get; init; } = null!;

    /// <summary>
    /// The frontend base the verified callback 302-redirects to (the payment
    /// result page); only the opaque result token is appended.
    /// </summary>
    public Uri FrontendResultBaseUrl { get; init; } = null!;

    /// <summary>Default outbound timeout for provider calls, in seconds (10).</summary>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
}
