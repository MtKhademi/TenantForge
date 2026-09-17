using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopOrderItemMap : IEntityTypeConfiguration<ShopOrderItem>
{
    public void Configure(EntityTypeBuilder<ShopOrderItem> builder)
    {
        builder.ToTable("shop_order_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(item => item.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductNameSnapshot).HasColumnName("product_name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(item => item.VariantLabelSnapshot).HasColumnName("variant_label_snapshot").HasMaxLength(120).IsRequired();
        builder.Property(item => item.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(item => item.Quantity).HasColumnName("quantity").IsRequired();

        builder.HasIndex(item => item.OrderId)
            .HasDatabaseName("ix_shop_order_items_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
