using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopPaymentInitiationMap : IEntityTypeConfiguration<ShopPaymentInitiation>
{
    public void Configure(EntityTypeBuilder<ShopPaymentInitiation> builder)
    {
        builder.ToTable("shop_payment_initiations");

        builder.HasKey(initiation => initiation.Id);

        builder.Property(initiation => initiation.Id)
            .HasColumnName("id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .ValueGeneratedNever();

        builder.Property(initiation => initiation.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(initiation => initiation.OrderId)
            .HasColumnName("order_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(initiation => initiation.AttemptId)
            .HasColumnName("attempt_id")
            .HasConversion(ShopTsidValueConverter.Shared)
            .IsRequired();

        builder.Property(initiation => initiation.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(initiation => initiation.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(64)
            .IsRequired();

        // The gateway's redirect URL, verbatim. 2048 covers an absolute
        // provider URL with room; the sandbox's relative path is far shorter.
        builder.Property(initiation => initiation.RedirectUrl)
            .HasColumnName("redirect_url")
            .HasMaxLength(2048)
            .IsRequired();

        // 32 random bytes; the raw callback token is SHA256 of this seed. The
        // seed is not the token and never authenticates anything — it only
        // lets a same-key retry re-derive the identical response.
        builder.Property(initiation => initiation.TokenSeed)
            .HasColumnName("token_seed")
            .IsRequired();

        builder.Property(initiation => initiation.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // The idempotency guarantee: the same key can never be stored twice for
        // the same tenant. This is the constraint the feature relies on to
        // resolve a racing first-call to exactly one winner — it is not a bare
        // earlier existence check.
        builder.HasIndex(initiation => new { initiation.TenantId, initiation.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_shop_payment_initiations_tenant_idempotency_key");

        builder.HasIndex(initiation => initiation.OrderId)
            .HasDatabaseName("ix_shop_payment_initiations_order_id");

        builder.HasOne<ShopOrder>()
            .WithMany()
            .HasForeignKey(initiation => initiation.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
