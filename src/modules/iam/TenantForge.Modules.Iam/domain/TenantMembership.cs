namespace TenantForge.Modules.Iam.Domain;

internal sealed class TenantMembership
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid AccountId { get; private set; }
    public TenantMembershipRole Role { get; private set; } = TenantMembershipRole.Owner;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private TenantMembership()
    {
    }

    public static TenantMembership CreateOwner(Guid tenantId, Guid accountId, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id is required.", nameof(accountId));
        }

        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            Role = TenantMembershipRole.Owner,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
