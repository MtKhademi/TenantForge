using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCartItem
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid CartId { get; private set; }
    public Tsid ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPriceSnapshot { get; private set; }

    private ShopCartItem()
    {
    }

    public static ShopCartItem Create(Tsid cartId, Tsid productVariantId, int quantity, decimal unitPriceSnapshot)
    {
        if (TsidId.IsDefault(cartId))
        {
            throw new ArgumentException("Cart id is required.", nameof(cartId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        return new ShopCartItem
        {
            Id = TsidId.NewId(),
            CartId = cartId,
            ProductVariantId = productVariantId,
            Quantity = quantity,
            UnitPriceSnapshot = unitPriceSnapshot
        };
    }

    public void SetQuantity(int quantity) => Quantity = quantity;
}
