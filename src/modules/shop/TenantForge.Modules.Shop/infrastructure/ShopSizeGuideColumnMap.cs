using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideColumnMap : IEntityTypeConfiguration<ShopSizeGuideColumn>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideColumn> builder)
    {
        builder.ToTable("shop_size_guide_columns");

        builder.HasKey(column => column.Id);

        builder.Property(column => column.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(column => column.ProductId)
            .HasColumnName("product_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(column => column.Name)
            .HasColumnName("name")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(column => column.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.HasIndex(column => column.ProductId)
            .HasDatabaseName("ix_shop_size_guide_columns_product_id");

        builder.HasOne<ShopProduct>()
            .WithMany()
            .HasForeignKey(column => column.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
