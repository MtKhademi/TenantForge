using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopOrderOperationMap : IEntityTypeConfiguration<ShopOrderOperation>
{
    public void Configure(EntityTypeBuilder<ShopOrderOperation> builder)
    {
        builder.ToTable("shop_order_operations");

        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(operation => operation.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(operation => operation.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(operation => operation.Key)
            .HasColumnName("idempotency_key")
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(operation => operation.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(operation => operation.ResponseSnapshot)
            .HasColumnName("response_snapshot")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(operation => operation.ActorId)
            .HasColumnName("actor_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(operation => operation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // The idempotency guarantee: the same key can never be stored twice for
        // the same tenant. This is the constraint the feature relies on to
        // resolve a racing first-call to exactly one winner — it is not a bare
        // earlier existence check.
        builder.HasIndex(operation => new { operation.TenantId, operation.Key })
            .IsUnique()
            .HasDatabaseName("ix_shop_order_operations_tenant_idempotency_key");

        builder.HasIndex(operation => operation.OrderId)
            .HasDatabaseName("ix_shop_order_operations_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(operation => operation.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
