using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class TenantRoleMap : IEntityTypeConfiguration<TenantRole>
{
    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("iam_tenant_roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasColumnName("id");
        builder.Property(role => role.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(role => role.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(role => role.NormalizedName).HasColumnName("normalized_name").HasMaxLength(80).IsRequired();
        builder.Property(role => role.Description).HasColumnName("description").HasMaxLength(200).IsRequired();
        builder.Property(role => role.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(role => role.PermissionKeys).HasColumnName("permission_keys").HasColumnType("text[]").IsRequired();
        builder.Property(role => role.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(role => role.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.HasIndex(role => new { role.TenantId, role.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ix_iam_tenant_roles_tenant_normalized_name");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(role => role.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
