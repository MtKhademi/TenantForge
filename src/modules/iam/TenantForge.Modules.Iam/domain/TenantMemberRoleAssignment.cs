using TSID.Creator.NET;

namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantMemberRoleAssignment
{
    public Tsid TenantMembershipId { get; private set; }
    public Tsid TenantRoleId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private TenantMemberRoleAssignment()
    {
    }

    public static TenantMemberRoleAssignment Create(Tsid tenantMembershipId, Tsid tenantRoleId, DateTimeOffset nowUtc)
    {
        return new TenantMemberRoleAssignment
        {
            TenantMembershipId = tenantMembershipId,
            TenantRoleId = tenantRoleId,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
