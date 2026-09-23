using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal enum ShopDiscountType
{
    Percentage,
    FixedAmount
}

internal sealed class ShopCoupon
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public ShopDiscountType DiscountType { get; private set; }
    public decimal DiscountValue { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>B041: the order's goods subtotal must reach this for the coupon to apply (0 = none).</summary>
    public decimal MinimumSubtotal { get; private set; }

    /// <summary>B041: caps the computed discount after the percentage/fixed amount is worked out (null = no cap).</summary>
    public decimal? MaximumDiscountAmount { get; private set; }

    /// <summary>B041: total redemptions allowed for this coupon (null = unlimited).</summary>
    public int? RedemptionLimit { get; private set; }

    /// <summary>B041: how many times this coupon has been redeemed; only ever incremented atomically inside order creation.</summary>
    public int RedeemedCount { get; private set; }

    /// <summary>B041: client-managed optimistic-concurrency counter (starts at 0, bumped on every admin save — an update or a deactivate).</summary>
    public int Version { get; private set; }

    private ShopCoupon()
    {
    }

    public static ShopCoupon Create(
        Tsid tenantId,
        string code,
        ShopDiscountType discountType,
        decimal discountValue,
        decimal minimumSubtotal,
        decimal? maximumDiscountAmount,
        int? redemptionLimit,
        DateTimeOffset? expiresAtUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Coupon code is required.", nameof(code));
        }

        var trimmedCode = code.Trim();
        return new ShopCoupon
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Code = trimmedCode,
            NormalizedCode = trimmedCode.ToUpperInvariant(),
            DiscountType = discountType,
            DiscountValue = discountValue,
            MinimumSubtotal = minimumSubtotal,
            MaximumDiscountAmount = maximumDiscountAmount,
            RedemptionLimit = redemptionLimit,
            RedeemedCount = 0,
            Version = 0,
            IsActive = true,
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime()
        };
    }

    /// <summary>
    /// B041: applies an admin update. The coupon's normalized code and discount
    /// type are deliberately not parameters — the update request cannot change
    /// them (Spec step 10c, satisfied by the request shape itself). Only the
    /// editable fields change, and <see cref="Version"/> is bumped exactly once.
    /// </summary>
    public void Update(
        decimal discountValue,
        decimal minimumSubtotal,
        decimal? maximumDiscountAmount,
        int? redemptionLimit,
        DateTimeOffset? expiresAtUtc,
        bool isActive)
    {
        DiscountValue = discountValue;
        MinimumSubtotal = minimumSubtotal;
        MaximumDiscountAmount = maximumDiscountAmount;
        RedemptionLimit = redemptionLimit;
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        IsActive = isActive;
        Version++;
    }

    /// <summary>B041: records one redemption. Called only inside order creation's transaction, under the coupon row lock.</summary>
    public void RecordRedemption() => RedeemedCount++;

    /// <summary>
    /// B041: deactivates the coupon. Bumps <see cref="Version"/> too — a
    /// deactivate is a state change, so it must advance the optimistic-
    /// concurrency guard exactly like an admin update does (B039's rule),
    /// otherwise a deactivate landing between an admin's list-read and its PUT
    /// would slip past the ExpectedVersion check.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        Version++;
    }
}
