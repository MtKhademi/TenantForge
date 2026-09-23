using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Features.Coupons;

/// <summary>
/// B041: the result of centralizing every coupon rule in one place.
/// <see cref="IsValid"/> is true only when the coupon passed every check and
/// <see cref="DiscountAmount"/> holds the amount to apply (already capped).
/// </summary>
internal sealed record CouponEvaluation(bool IsValid, decimal DiscountAmount, string? ErrorCode);

/// <summary>
/// B041: the single owner of every coupon rule. <see cref="Evaluate"/> is pure —
/// it reads the already-loaded <see cref="ShopCoupon"/> and computes a discount,
/// it never writes. The "preview vs consume" distinction lives in the caller:
/// checkout summary calls it and renders the result (writes nothing); order
/// creation calls it inside its transaction and, only on a valid result, bumps
/// <c>RedeemedCount</c> itself under the coupon row lock.
///
/// <see cref="CouponNotFound"/> is the one reason Evaluate never produces: the
/// caller looks the coupon up with a tenant-first predicate and, when the lookup
/// is null, returns that code itself without calling Evaluate (so a code that
/// "does not exist" and a code that "belongs to another tenant" are indistinguishable).
/// </summary>
internal static class ShopCouponPolicy
{
    /// <summary>Stable reason codes — surfaced verbatim in the <c>couponCode</c> field error.</summary>
    internal const string CouponNotFound = "coupon_not_found";
    internal const string CouponInactive = "coupon_inactive";
    internal const string CouponExpired = "coupon_expired";
    internal const string CouponMinimumNotMet = "coupon_minimum_not_met";
    internal const string CouponLimitReached = "coupon_limit_reached";

    /// <summary>
    /// Checks the rules in a fixed order, stopping at the first failure. The
    /// input <paramref name="coupon"/> is assumed non-null and already tenant-
    /// scoped by the caller (see the class summary).
    /// </summary>
    internal static CouponEvaluation Evaluate(ShopCoupon coupon, decimal subtotal, DateTimeOffset nowUtc)
    {
        if (!coupon.IsActive)
        {
            return new CouponEvaluation(false, 0, CouponInactive);
        }

        // "now is past it": strictly past, so a coupon whose ExpiresAtUtc equals
        // now is still valid — matching the pre-B041 `ExpiresAtUtc < now` check.
        if (coupon.ExpiresAtUtc is { } expiresAt && nowUtc > expiresAt)
        {
            return new CouponEvaluation(false, 0, CouponExpired);
        }

        if (subtotal < coupon.MinimumSubtotal)
        {
            return new CouponEvaluation(false, 0, CouponMinimumNotMet);
        }

        if (coupon.RedemptionLimit is { } limit && coupon.RedeemedCount >= limit)
        {
            return new CouponEvaluation(false, 0, CouponLimitReached);
        }

        // All gates passed: compute the raw discount (percentage or fixed), then
        // clamp it down first to the optional maximum, then never past the
        // subtotal itself — the applied discount can never exceed the goods.
        var raw = coupon.DiscountType == ShopDiscountType.Percentage
            ? Math.Round(subtotal * coupon.DiscountValue / 100m, 2)
            : coupon.DiscountValue;

        var discount = coupon.MaximumDiscountAmount is { } maximum
            ? Math.Min(raw, maximum)
            : raw;

        discount = Math.Min(discount, subtotal);

        return new CouponEvaluation(true, discount, null);
    }

    /// <summary>
    /// B041: the single public-facing message for a stable reason code. Every
    /// message embeds the exact code (in parentheses) so it is machine-assertable
    /// on the wire, and the three reasons that predate B041 keep the "not valid"
    /// phrasing B030/B031's tests lock in. <see cref="CouponNotFound"/> is the
    /// one message the caller produces for both "does not exist" and "belongs to
    /// another tenant" — the two must never be told apart.
    /// </summary>
    internal static string MessageFor(string errorCode) => errorCode switch
    {
        CouponNotFound => "This coupon code is not valid. (coupon_not_found)",
        CouponInactive => "This coupon code is not valid. (coupon_inactive)",
        CouponExpired => "This coupon code is not valid. (coupon_expired)",
        CouponMinimumNotMet => "This coupon requires a higher subtotal. (coupon_minimum_not_met)",
        CouponLimitReached => "This coupon has reached its redemption limit. (coupon_limit_reached)",
        _ => "This coupon code is not valid. (coupon_not_found)"
    };
}
