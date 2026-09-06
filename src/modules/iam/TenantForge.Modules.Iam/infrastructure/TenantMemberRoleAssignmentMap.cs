using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenantForge.Modules.Iam.Domain;

namespace TenantForge.Modules.Iam.Infrastructure;

internal sealed class TenantMemberRoleAssignmentMap : IEntityTypeConfiguration<TenantMemberRoleAssignment>
{
    public void Configure(EntityTypeBuilder<TenantMemberRoleAssignment> builder)
    {
        builder.ToTable("iam_tenant_member_role_assignments");
        builder.HasKey(assignment => new { assignment.TenantMembershipId, assignment.TenantRoleId });
        builder.Property(assignment => assignment.TenantMembershipId).HasColumnName("tenant_membership_id");
        builder.Property(assignment => assignment.TenantRoleId).HasColumnName("tenant_role_id");
        builder.Property(assignment => assignment.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasOne<TenantMembership>().WithMany().HasForeignKey(assignment => assignment.TenantMembershipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TenantRole>().WithMany().HasForeignKey(assignment => assignment.TenantRoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
