using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantMembership
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid AccountId { get; private set; }
    public TenantMembershipRole Role { get; private set; } = TenantMembershipRole.Owner;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private TenantMembership()
    {
    }

    public static TenantMembership CreateOwner(Tsid tenantId, Tsid accountId, DateTimeOffset nowUtc) =>
        Create(tenantId, accountId, TenantMembershipRole.Owner, nowUtc);

    public static TenantMembership CreateMember(Tsid tenantId, Tsid accountId, DateTimeOffset nowUtc) =>
        Create(tenantId, accountId, TenantMembershipRole.Member, nowUtc);

    private static TenantMembership Create(Tsid tenantId, Tsid accountId, TenantMembershipRole role, DateTimeOffset nowUtc)
    {
        if (TsidId.IsDefault(tenantId))
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (TsidId.IsDefault(accountId))
        {
            throw new ArgumentException("Account id is required.", nameof(accountId));
        }

        return new TenantMembership
        {
            Id = TsidId.NewId(),
            TenantId = tenantId,
            AccountId = accountId,
            Role = role,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
