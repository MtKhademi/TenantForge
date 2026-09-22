using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopProductMap : IEntityTypeConfiguration<ShopProduct>
{
    public void Configure(EntityTypeBuilder<ShopProduct> builder)
    {
        builder.ToTable("shop_products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(product => product.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(product => product.CategoryId)
            .HasColumnName("category_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Slug)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(product => product.BasePrice)
            .HasColumnName("base_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(product => product.CompareAtPrice)
            .HasColumnName("compare_at_price")
            .HasColumnType("numeric(12,2)");

        builder.Property(product => product.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(product => product.GalleryVersion)
            .HasColumnName("gallery_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.HasIndex(product => new { product.TenantId, product.Slug })
            .IsUnique()
            .HasDatabaseName("ix_shop_products_tenant_slug");

        builder.HasIndex(product => product.CategoryId)
            .HasDatabaseName("ix_shop_products_category_id");

        builder.HasOne<ShopCategory>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
