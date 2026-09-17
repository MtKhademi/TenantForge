using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopShippingRate
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string ProvinceName { get; private set; } = string.Empty;
    public decimal Cost { get; private set; }

    private ShopShippingRate()
    {
    }

    public static ShopShippingRate Create(Tsid tenantId, string provinceName, decimal cost)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(provinceName))
        {
            throw new ArgumentException("Province name is required.", nameof(provinceName));
        }

        return new ShopShippingRate
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            ProvinceName = provinceName.Trim(),
            Cost = cost
        };
    }

    public void UpdateCost(decimal cost) => Cost = cost;
}
