using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopSizeGuideCell
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid RowId { get; private set; }
    public Tsid ColumnId { get; private set; }
    public string Value { get; private set; } = string.Empty;

    private ShopSizeGuideCell()
    {
    }

    public static ShopSizeGuideCell Create(Tsid rowId, Tsid columnId, string value)
    {
        if (TsidId.IsDefault(rowId))
        {
            throw new ArgumentException("Row id is required.", nameof(rowId));
        }

        if (TsidId.IsDefault(columnId))
        {
            throw new ArgumentException("Column id is required.", nameof(columnId));
        }

        return new ShopSizeGuideCell
        {
            Id = TsidId.NewId(),
            RowId = rowId,
            ColumnId = columnId,
            Value = value?.Trim() ?? string.Empty
        };
    }
}
