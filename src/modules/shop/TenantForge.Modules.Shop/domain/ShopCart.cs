using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCart
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid? CouponId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public ShopCartStatus Status { get; private set; } = ShopCartStatus.Active;
    public DateTimeOffset LastTouchedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    private ShopCart()
    {
    }

    public static ShopCart Create(Tsid tenantId, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        var universalNow = nowUtc.ToUniversalTime();
        return new ShopCart
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            CouponId = null,
            CreatedAtUtc = universalNow,
            Status = ShopCartStatus.Active,
            LastTouchedAtUtc = universalNow,
            ExpiresAtUtc = expiresAtUtc.ToUniversalTime(),
            ClosedAtUtc = null
        };
    }

    public void ExtendLease(DateTimeOffset nowUtc, TimeSpan leaseDuration)
    {
        if (Status != ShopCartStatus.Active)
        {
            return;
        }

        var universalNow = nowUtc.ToUniversalTime();
        LastTouchedAtUtc = universalNow;
        ExpiresAtUtc = universalNow.Add(leaseDuration);
    }

    public void MarkExpired(DateTimeOffset nowUtc)
    {
        if (Status != ShopCartStatus.Active)
        {
            return;
        }

        Status = ShopCartStatus.Expired;
        ClosedAtUtc = nowUtc.ToUniversalTime();
    }

    public void MarkConverted(DateTimeOffset nowUtc)
    {
        if (Status != ShopCartStatus.Active)
        {
            return;
        }

        Status = ShopCartStatus.Converted;
        ClosedAtUtc = nowUtc.ToUniversalTime();
    }

    /// <summary>Set in B030's checkout-summary task; unused until then.</summary>
    public void ApplyCoupon(Tsid? couponId) => CouponId = couponId;
}
