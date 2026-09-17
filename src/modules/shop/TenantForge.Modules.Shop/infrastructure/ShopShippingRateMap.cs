using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopShippingRateMap : IEntityTypeConfiguration<ShopShippingRate>
{
    public void Configure(EntityTypeBuilder<ShopShippingRate> builder)
    {
        builder.ToTable("shop_shipping_rates");

        builder.HasKey(rate => rate.Id);

        builder.Property(rate => rate.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(rate => rate.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(rate => rate.ProvinceName)
            .HasColumnName("province_name")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(rate => rate.Cost)
            .HasColumnName("cost")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.HasIndex(rate => new { rate.TenantId, rate.ProvinceName })
            .IsUnique()
            .HasDatabaseName("ix_shop_shipping_rates_tenant_province");
    }
}
