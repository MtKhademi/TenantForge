using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCart
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid? CouponId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private ShopCart()
    {
    }

    public static ShopCart Create(Tsid tenantId, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ShopCart
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            CouponId = null,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }

    /// <summary>Set in B030's checkout-summary task; unused until then.</summary>
    public void ApplyCoupon(Tsid? couponId) => CouponId = couponId;
}
