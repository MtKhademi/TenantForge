using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideColumn
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    private ShopSizeGuideColumn()
    {
    }

    public static ShopSizeGuideColumn Create(Tsid productId, string name, int displayOrder)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Column name is required.", nameof(name));
        }

        return new ShopSizeGuideColumn
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            Name = name.Trim(),
            DisplayOrder = displayOrder
        };
    }
}
