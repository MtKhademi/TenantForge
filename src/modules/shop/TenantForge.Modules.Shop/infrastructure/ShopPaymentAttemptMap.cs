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

        // B044: the amount frozen at initiation — same money column type the
        // order's own totals use.
        builder.Property(attempt => attempt.AmountSnapshot)
            .HasColumnName("amount_snapshot")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        // B044: SHA-256 (32 bytes) hex-encoded → exactly 64 lowercase chars.
        // The raw callback token is never persisted.
        builder.Property(attempt => attempt.CallbackTokenHash)
            .HasColumnName("callback_token_hash")
            .HasMaxLength(64)
            .IsRequired();

        // B044: stable failure reason code (e.g. "payment_declined").
        builder.Property(attempt => attempt.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(40);

        // B044: the provider's verification-time reference (ZarinPal's RefId
        // in B045). Never the authority — that is gateway_reference.
        builder.Property(attempt => attempt.ProviderReference)
            .HasColumnName("provider_reference")
            .HasMaxLength(60);

        builder.Property(attempt => attempt.VerifiedAtUtc)
            .HasColumnName("verified_at_utc");

        // B044: optimistic-concurrency / row-locking token, bumped by the
        // completion service exactly when it resolves the attempt.
        builder.Property(attempt => attempt.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
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
