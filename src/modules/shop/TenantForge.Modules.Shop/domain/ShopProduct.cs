using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopProduct
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal BasePrice { get; private set; }
    public decimal? CompareAtPrice { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ShopProduct()
    {
    }

    public static ShopProduct Create(
        Tsid tenantId,
        Tsid categoryId,
        string name,
        string slug,
        string description,
        decimal basePrice,
        decimal? compareAtPrice)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Product slug is required.", nameof(slug));
        }

        return new ShopProduct
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description.Trim(),
            BasePrice = basePrice,
            CompareAtPrice = compareAtPrice,
            IsActive = true
        };
    }

    public void Update(
        Tsid categoryId,
        string name,
        string slug,
        string description,
        decimal basePrice,
        decimal? compareAtPrice,
        bool isActive)
    {
        CategoryId = categoryId;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description.Trim();
        BasePrice = basePrice;
        CompareAtPrice = compareAtPrice;
        IsActive = isActive;
    }
}
