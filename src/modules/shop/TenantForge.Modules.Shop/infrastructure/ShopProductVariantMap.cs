using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProductVariantMap : IEntityTypeConfiguration<ShopProductVariant>
{
    public void Configure(EntityTypeBuilder<ShopProductVariant> builder)
    {
        builder.ToTable("shop_product_variants");

        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(variant => variant.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(variant => variant.Color)
            .HasColumnName("color")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(variant => variant.Size)
            .HasColumnName("size")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(variant => variant.Sku)
            .HasColumnName("sku")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(variant => variant.StockQuantity)
            .HasColumnName("stock_quantity")
            .IsRequired();

        builder.Property(variant => variant.PriceOverride)
            .HasColumnName("price_override")
            .HasColumnType("numeric(12,2)");

        builder.HasIndex(variant => new { variant.ProductId, variant.Color, variant.Size })
            .IsUnique()
            .HasDatabaseName("ix_shop_product_variants_product_color_size");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
