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

    private ShopCoupon()
    {
    }

    public static ShopCoupon Create(
        Tsid tenantId,
        string code,
        ShopDiscountType discountType,
        decimal discountValue,
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
            IsActive = true,
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime()
        };
    }

    public void Deactivate() => IsActive = false;
}
