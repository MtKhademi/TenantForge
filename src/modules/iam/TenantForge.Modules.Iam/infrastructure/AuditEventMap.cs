using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class AuditEventMap : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("iam_audit_events");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.Id).HasColumnName("id");
        builder.Property(auditEvent => auditEvent.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(auditEvent => auditEvent.ActorAccountId).HasColumnName("actor_account_id").IsRequired();
        builder.Property(auditEvent => auditEvent.Actor).HasColumnName("actor").HasMaxLength(200).IsRequired();
        builder.Property(auditEvent => auditEvent.ActorEmail).HasColumnName("actor_email").HasMaxLength(254).IsRequired();
        builder.Property(auditEvent => auditEvent.Action).HasColumnName("action").HasMaxLength(80).IsRequired();
        builder.Property(auditEvent => auditEvent.Target).HasColumnName("target").HasMaxLength(254).IsRequired();
        builder.Property(auditEvent => auditEvent.Details).HasColumnName("details").HasMaxLength(500).IsRequired();
        builder.Property(auditEvent => auditEvent.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.CreatedAtUtc }).HasDatabaseName("ix_iam_audit_events_tenant_created");
        builder.HasIndex(auditEvent => new { auditEvent.TenantId, auditEvent.Action }).HasDatabaseName("ix_iam_audit_events_tenant_action");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(auditEvent => auditEvent.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Account>().WithMany().HasForeignKey(auditEvent => auditEvent.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
