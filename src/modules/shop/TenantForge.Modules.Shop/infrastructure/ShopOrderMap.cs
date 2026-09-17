using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopOrderMap : IEntityTypeConfiguration<ShopOrder>
{
    public void Configure(EntityTypeBuilder<ShopOrder> builder)
    {
        builder.ToTable("shop_orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(order => order.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(order => order.OrderNumber)
            .HasColumnName("order_number")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.TrackingCode)
            .HasColumnName("tracking_code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(order => order.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.CustomerName)
            .HasColumnName("customer_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(order => order.CustomerPhone)
            .HasColumnName("customer_phone")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.ShippingProvince)
            .HasColumnName("shipping_province")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(order => order.ShippingCity)
            .HasColumnName("shipping_city")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(order => order.ShippingAddressLine)
            .HasColumnName("shipping_address_line")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(order => order.ShippingPostalCode)
            .HasColumnName("shipping_postal_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(order => order.SubTotal).HasColumnName("sub_total").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.ShippingCost).HasColumnName("shipping_cost").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(order => order.GrandTotal).HasColumnName("grand_total").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(order => order.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(order => order.OrderNumber)
            .IsUnique()
            .HasDatabaseName("ix_shop_orders_order_number");

        builder.HasIndex(order => order.TrackingCode)
            .IsUnique()
            .HasDatabaseName("ix_shop_orders_tracking_code");

        builder.HasIndex(order => new { order.TenantId, order.TrackingCode, order.CustomerPhone })
            .HasDatabaseName("ix_shop_orders_tenant_tracking_phone");
    }
}
