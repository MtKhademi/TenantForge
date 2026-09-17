using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCartItemMap : IEntityTypeConfiguration<ShopCartItem>
{
    public void Configure(EntityTypeBuilder<ShopCartItem> builder)
    {
        builder.ToTable("shop_cart_items");

        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(item => item.CartId)
            .HasColumnName("cart_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.ProductVariantId)
            .HasColumnName("product_variant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(item => item.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(item => item.UnitPriceSnapshot)
            .HasColumnName("unit_price_snapshot")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.HasIndex(item => new { item.CartId, item.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("ix_shop_cart_items_cart_variant");

        builder.HasOne<ShopCart>()
            .WithMany()
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ShopProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
