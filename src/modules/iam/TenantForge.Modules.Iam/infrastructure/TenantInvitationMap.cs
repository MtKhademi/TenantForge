using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class TenantInvitationMap : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.ToTable("iam_tenant_invitations");
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).HasColumnName("id");
        builder.Property(invitation => invitation.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(invitation => invitation.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.Role).HasColumnName("role").HasMaxLength(80).IsRequired();
        builder.Property(invitation => invitation.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(invitation => invitation.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(invitation => invitation.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(invitation => new { invitation.TenantId, invitation.NormalizedEmail, invitation.Status })
            .HasDatabaseName("ix_iam_tenant_invitations_active_email");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(invitation => invitation.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
