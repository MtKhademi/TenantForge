using System.Globalization;

namespace TenantForge.Modules.Shop.Features.Payments.ZarinPal;

/// <summary>
/// B045: the single, checked conversion from an order's stored total to the
/// provider's required integer amount.
///
/// Every money value TenantForge stores is a <b>Toman</b> amount (the current
/// UI's unit — 1 IRT = 10 IRR). The conversion is exactly one step and uses
/// checked arithmetic at every stage:
///
/// - <c>IRT</c>: the provider amount (in Tomans) is the stored total itself;
/// - <c>IRR</c>: the provider amount (in Rials) is the stored total × 10 —
///   the multiplication and the final <see cref="long"/> cast both run in a
///   <c>checked</c> context and throw on overflow instead of wrapping;
/// - a total that is not an exact integer in the configured currency (for
///   example 1234.56 Tomans expressed in Rials would be 12345.6) is a
///   configuration/amount error and throws — it is never silently truncated.
///
/// The caller (the gateway) maps a throw to a safe initiation failure; an
/// unpayable amount must never be sent to the provider.
/// </summary>
internal static class ZarinPalAmountConverter
{
    public static long ToProviderAmount(decimal orderTotalToman, string currency)
    {
        if (orderTotalToman < 0m)
        {
            throw new InvalidOperationException("The order total must not be negative.");
        }

        decimal inProviderUnits = currency switch
        {
            "IRT" => orderTotalToman,
            "IRR" => checked(orderTotalToman * 10m),
            _ => throw new InvalidOperationException(
                $"The currency '{currency ?? string.Empty}' is not a supported ZarinPal currency; expected one of {string.Join(", ", ZarinPalOptions.SupportedCurrencies)}.")
        };

        if (decimal.Truncate(inProviderUnits) != inProviderUnits)
        {
            throw new InvalidOperationException(
                "The order total is not an exact integer amount in the configured provider currency.");
        }

        return checked((long)inProviderUnits);
    }

    /// <summary>
    /// Formats the provider integer amount for the JSON body using the
    /// invariant culture — the provider API never accepts a locale-formatted
    /// number.
    /// </summary>
    public static string FormatProviderAmount(long providerAmount)
        => providerAmount.ToString(CultureInfo.InvariantCulture);
}
