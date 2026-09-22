using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProfileMap : IEntityTypeConfiguration<ShopProfile>
{
    public void Configure(EntityTypeBuilder<ShopProfile> builder)
    {
        builder.ToTable("shop_profiles");

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(profile => profile.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(profile => profile.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(profile => profile.Tagline)
            .HasColumnName("tagline")
            .HasMaxLength(180)
            .IsRequired();

        builder.Property(profile => profile.SupportPhone)
            .HasColumnName("support_phone")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(profile => profile.InstagramUrl)
            .HasColumnName("instagram_url")
            .HasMaxLength(300);

        builder.Property(profile => profile.AboutText)
            .HasColumnName("about_text")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(profile => profile.ShippingPolicy)
            .HasColumnName("shipping_policy")
            .HasMaxLength(6000)
            .IsRequired();

        builder.Property(profile => profile.PaymentPolicy)
            .HasColumnName("payment_policy")
            .HasMaxLength(6000)
            .IsRequired();

        builder.Property(profile => profile.ReturnPolicy)
            .HasColumnName("return_policy")
            .HasMaxLength(6000)
            .IsRequired();

        builder.Property(profile => profile.PrivacyPolicy)
            .HasColumnName("privacy_policy")
            .HasMaxLength(6000)
            .IsRequired();

        builder.Property(profile => profile.IsPublished)
            .HasColumnName("is_published")
            .IsRequired();

        builder.Property(profile => profile.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(profile => profile.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(profile => profile.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        // B039: exactly one profile row per tenant — the database constraint
        // that resolves concurrent first-creates to one success.
        builder.HasIndex(profile => profile.TenantId)
            .IsUnique()
            .HasDatabaseName("ix_shop_profiles_tenant_id");
    }
}
