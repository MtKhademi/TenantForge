namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: a safe, stable-code failure from the ZarinPal provider call. The
/// message carries ONLY the stable code — never the provider's response text
/// (which may echo request fields), the merchant id or any secret — so it is
/// safe to log and to surface in a 5xx body.
///
/// Stable codes:
/// - <c>provider_unavailable</c> — timeout, connection failure, 5xx, or a
///   malformed/unparsable response body;
/// - <c>provider_rejected</c> — the provider answered with a non-100 request
///   code (a business rejection);
/// - <c>authority_missing</c> — request code 100 but an empty authority (treated
///   as a failure even though the code was 100);
/// - <c>amount_invalid</c> — the order total cannot be expressed as an exact
///   integer in the configured currency (checked-arithmetic or fraction
///   failure); the initiation is refused before any provider call.
/// </summary>
internal sealed class ZarinPalProviderException(string stableCode) : Exception($"ZarinPal provider failure: {stableCode}")
{
    public string StableCode { get; } = stableCode;
}
