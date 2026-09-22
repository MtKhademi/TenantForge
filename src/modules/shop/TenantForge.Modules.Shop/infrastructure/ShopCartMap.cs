using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCartMap : IEntityTypeConfiguration<ShopCart>
{
    public void Configure(EntityTypeBuilder<ShopCart> builder)
    {
        builder.ToTable("shop_carts");

        builder.HasKey(cart => cart.Id);

        builder.Property(cart => cart.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(cart => cart.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cart => cart.CouponId)
            .HasColumnName("coupon_id")
            .HasConversion(ShopTsidValueConverter.Shared);

        builder.Property(cart => cart.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(cart => cart.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(cart => cart.LastTouchedAtUtc)
            .HasColumnName("last_touched_at_utc")
            .IsRequired();

        builder.Property(cart => cart.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();

        builder.Property(cart => cart.ClosedAtUtc)
            .HasColumnName("closed_at_utc");

        builder.HasIndex(cart => new { cart.Status, cart.ExpiresAtUtc })
            .HasDatabaseName("ix_shop_carts_status_expires_at_utc");
    }
}
