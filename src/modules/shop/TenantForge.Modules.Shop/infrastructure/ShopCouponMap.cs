using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCouponMap : IEntityTypeConfiguration<ShopCoupon>
{
    public void Configure(EntityTypeBuilder<ShopCoupon> builder)
    {
        builder.ToTable("shop_coupons");

        builder.HasKey(coupon => coupon.Id);

        builder.Property(coupon => coupon.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(coupon => coupon.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(coupon => coupon.Code)
            .HasColumnName("code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(coupon => coupon.NormalizedCode)
            .HasColumnName("normalized_code")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(coupon => coupon.DiscountType)
            .HasColumnName("discount_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(coupon => coupon.DiscountValue)
            .HasColumnName("discount_value")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(coupon => coupon.MinimumSubtotal)
            .HasColumnName("minimum_subtotal")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(coupon => coupon.MaximumDiscountAmount)
            .HasColumnName("maximum_discount_amount")
            .HasColumnType("numeric(12,2)");

        builder.Property(coupon => coupon.RedemptionLimit)
            .HasColumnName("redemption_limit");

        builder.Property(coupon => coupon.RedeemedCount)
            .HasColumnName("redeemed_count")
            .IsRequired();

        builder.Property(coupon => coupon.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(coupon => coupon.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(coupon => coupon.ExpiresAtUtc)
            .HasColumnName("expires_at_utc");

        builder.HasIndex(coupon => new { coupon.TenantId, coupon.NormalizedCode })
            .IsUnique()
            .HasDatabaseName("ix_shop_coupons_tenant_normalized_code");
    }
}
