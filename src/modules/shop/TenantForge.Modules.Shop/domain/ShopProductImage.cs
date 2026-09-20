using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Shop.Domain;

internal sealed class ShopProductImage
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid ProductId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "image/webp";
    public long ByteLength { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string AltText { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private ShopProductImage()
    {
    }

    public static ShopProductImage Create(
        Tsid tenantId,
        Tsid productId,
        string storageKey,
        long byteLength,
        int width,
        int height,
        string? altText,
        int displayOrder,
        DateTimeOffset createdAtUtc)
    {
        if (TsidId.IsDefault(tenantId)) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (TsidId.IsDefault(productId)) throw new ArgumentException("Product id is required.", nameof(productId));
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("Storage key is required.", nameof(storageKey));
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length must be positive.");
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        if (displayOrder < 0) throw new ArgumentOutOfRangeException(nameof(displayOrder), "Display order cannot be negative.");

        return new ShopProductImage
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            ProductId = productId,
            StorageKey = storageKey.Trim(),
            ContentType = "image/webp",
            ByteLength = byteLength,
            Width = width,
            Height = height,
            AltText = (altText ?? string.Empty).Trim(),
            DisplayOrder = displayOrder,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void MoveTo(int displayOrder)
    {
        if (displayOrder < 0) throw new ArgumentOutOfRangeException(nameof(displayOrder), "Display order cannot be negative.");
        DisplayOrder = displayOrder;
    }
}
