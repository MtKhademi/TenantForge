namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantMemberRoleAssignment
{
    public Guid TenantMembershipId { get; private set; }
    public Guid TenantRoleId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private TenantMemberRoleAssignment()
    {
    }

    public static TenantMemberRoleAssignment Create(Guid tenantMembershipId, Guid tenantRoleId, DateTimeOffset nowUtc)
    {
        return new TenantMemberRoleAssignment
        {
            TenantMembershipId = tenantMembershipId,
            TenantRoleId = tenantRoleId,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
