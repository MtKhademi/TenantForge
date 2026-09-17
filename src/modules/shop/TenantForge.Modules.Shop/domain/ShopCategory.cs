using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopCategory
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ShopCategory()
    {
    }

    public static ShopCategory Create(Tsid tenantId, string name, string slug, int displayOrder)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Category slug is required.", nameof(slug));
        }

        return new ShopCategory
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            DisplayOrder = displayOrder,
            IsActive = true
        };
    }

    public void Update(string name, string slug, int displayOrder, bool isActive)
    {
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
