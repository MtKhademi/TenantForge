using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopPaymentAttemptMap : IEntityTypeConfiguration<ShopPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<ShopPaymentAttempt> builder)
    {
        builder.ToTable("shop_payment_attempts");

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(attempt => attempt.Provider)
            .HasColumnName("provider")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(attempt => attempt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(attempt => attempt.GatewayReference)
            .HasColumnName("gateway_reference")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(attempt => attempt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(attempt => attempt.CallbackReceivedAtUtc)
            .HasColumnName("callback_received_at_utc");

        builder.HasIndex(attempt => attempt.GatewayReference)
            .IsUnique()
            .HasDatabaseName("ix_shop_payment_attempts_gateway_reference");

        builder.HasIndex(attempt => attempt.OrderId)
            .HasDatabaseName("ix_shop_payment_attempts_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(attempt => attempt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
