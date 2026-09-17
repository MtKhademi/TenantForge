using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopSizeGuideCellMap : IEntityTypeConfiguration<ShopSizeGuideCell>
{
    public void Configure(EntityTypeBuilder<ShopSizeGuideCell> builder)
    {
        builder.ToTable("shop_size_guide_cells");

        builder.HasKey(cell => cell.Id);

        builder.Property(cell => cell.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(cell => cell.RowId)
            .HasColumnName("row_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cell => cell.ColumnId)
            .HasColumnName("column_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(cell => cell.Value)
            .HasColumnName("value")
            .HasMaxLength(60)
            .IsRequired();

        builder.HasIndex(cell => new { cell.RowId, cell.ColumnId })
            .IsUnique()
            .HasDatabaseName("ix_shop_size_guide_cells_row_column");

        builder.HasOne<ShopSizeGuideRow>()
            .WithMany()
            .HasForeignKey(cell => cell.RowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ShopSizeGuideColumn>()
            .WithMany()
            .HasForeignKey(cell => cell.ColumnId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
