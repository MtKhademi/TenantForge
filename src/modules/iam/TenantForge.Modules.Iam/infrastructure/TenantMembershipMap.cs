using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class TenantMembershipMap : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("iam_tenant_memberships");

        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .HasColumnName("id");

        builder.Property(membership => membership.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(membership => membership.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(membership => membership.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(membership => membership.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(membership => new { membership.TenantId, membership.AccountId })
            .IsUnique()
            .HasDatabaseName("ix_iam_tenant_memberships_tenant_account");

        builder.HasIndex(membership => membership.AccountId)
            .HasDatabaseName("ix_iam_tenant_memberships_account_id");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(membership => membership.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
