using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopOrderItem
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid OrderId { get; private set; }
    public Tsid ProductVariantId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = string.Empty;
    public string VariantLabelSnapshot { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    private ShopOrderItem()
    {
    }

    public static ShopOrderItem Create(
        Tsid orderId,
        Tsid productVariantId,
        string productNameSnapshot,
        string variantLabelSnapshot,
        decimal unitPrice,
        int quantity)
    {
        if (TsidId.IsDefault(orderId))
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        return new ShopOrderItem
        {
            Id = TsidId.NewId(),
            OrderId = orderId,
            ProductVariantId = productVariantId,
            ProductNameSnapshot = productNameSnapshot,
            VariantLabelSnapshot = variantLabelSnapshot,
            UnitPrice = unitPrice,
            Quantity = quantity
        };
    }
}
