namespace TenantForge.Modules.Iam.Domain;

internal sealed class AuditEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string ActorEmail { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Target { get; private set; } = string.Empty;
    public string Details { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private AuditEvent()
    {
    }

    public static AuditEvent Create(Guid tenantId, Guid actorAccountId, string actor, string actorEmail, string action, string target, string details, DateTimeOffset nowUtc)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorAccountId = actorAccountId,
            Actor = actor.Trim(),
            ActorEmail = actorEmail.Trim().ToLowerInvariant(),
            Action = action,
            Target = target,
            Details = details,
            CreatedAtUtc = nowUtc.ToUniversalTime()
        };
    }
}
