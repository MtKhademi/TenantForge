using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideRowMap : IEntityTypeConfiguration<ShopSizeGuideRow>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideRow> builder)
    {
        builder.ToTable("shop_size_guide_rows");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(row => row.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(row => row.SizeLabel)
            .HasColumnName("size_label")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(row => row.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.HasIndex(row => row.ProductId)
            .HasDatabaseName("ix_shop_size_guide_rows_product_id");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(row => row.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
