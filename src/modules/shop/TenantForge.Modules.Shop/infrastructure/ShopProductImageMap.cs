using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProductImageMap : IEntityTypeConfiguration<ShopProductImage>
{
    public void Configure(EntityTypeBuilder<ShopProductImage> builder)
    {
        builder.ToTable("shop_product_images");

        builder.HasKey(image => image.Id);

        builder.Property(image => image.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(image => image.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(image => image.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(image => image.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(image => image.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(image => image.ByteLength)
            .HasColumnName("byte_length")
            .IsRequired();

        builder.Property(image => image.Width)
            .HasColumnName("width")
            .IsRequired();

        builder.Property(image => image.Height)
            .HasColumnName("height")
            .IsRequired();

        builder.Property(image => image.AltText)
            .HasColumnName("alt_text")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(image => image.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(image => image.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(image => new { image.ProductId, image.DisplayOrder })
            .IsUnique()
            .HasDatabaseName("ux_shop_product_images_product_display_order");

        builder.HasIndex(image => new { image.TenantId, image.ProductId })
            .HasDatabaseName("ix_shop_product_images_tenant_product");

        builder.HasIndex(image => image.StorageKey)
            .IsUnique()
            .HasDatabaseName("ux_shop_product_images_storage_key");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
