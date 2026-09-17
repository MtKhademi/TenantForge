using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopCategoryMap : IEntityTypeConfiguration<ShopCategory>
{
    public void Configure(EntityTypeBuilder<ShopCategory> builder)
    {
        builder.ToTable("shop_categories");

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(category => category.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.Slug)
            .HasColumnName("slug")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(category => category.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(category => category.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(category => new { category.TenantId, category.Slug })
            .IsUnique()
            .HasDatabaseName("ix_shop_categories_tenant_slug");
    }
}
