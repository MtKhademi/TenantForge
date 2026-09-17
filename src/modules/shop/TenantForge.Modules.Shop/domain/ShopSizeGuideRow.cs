using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideRow
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid ProductId { get; private set; }
    public string SizeLabel { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    private ShopSizeGuideRow()
    {
    }

    public static ShopSizeGuideRow Create(Tsid productId, string sizeLabel, int displayOrder)
    {
        if (TsidId.IsDefault(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(sizeLabel))
        {
            throw new ArgumentException("Size label is required.", nameof(sizeLabel));
        }

        return new ShopSizeGuideRow
        {
            Id = TsidId.NewId(),
            ProductId = productId,
            SizeLabel = sizeLabel.Trim(),
            DisplayOrder = displayOrder
        };
    }
}
