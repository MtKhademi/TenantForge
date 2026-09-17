using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopProductVariant
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string Color { get; private set; } = string.Empty;
    public string Size { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public int StockQuantity { get; private set; }
    public decimal? PriceOverride { get; private set; }

    private ShopProductVariant()
    {
    }

    public static ShopProductVariant Create(
        Tsid productId,
        string color,
        string size,
        string sku,
        int stockQuantity,
        decimal? priceOverride)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (stockQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockQuantity), "Stock quantity cannot be negative.");
        }

        return new ShopProductVariant
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            Color = color.Trim(),
            Size = size.Trim(),
            Sku = sku.Trim(),
            StockQuantity = stockQuantity,
            PriceOverride = priceOverride
        };
    }

    /// <summary>
    /// Decrements stock by <paramref name="quantity"/>. Returns false (and
    /// changes nothing) when there is not enough stock, so the caller can
    /// answer with a clear "insufficient stock" result instead of allowing a
    /// negative row.
    /// </summary>
    public bool TryReserve(int quantity)
    {
        if (quantity <= 0 || StockQuantity < quantity)
        {
            return false;
        }

        StockQuantity -= quantity;
        return true;
    }
}
