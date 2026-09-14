using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;

namespace TenantForge.Modules.Iam.Domain;

internal sealed class AuditEvent
{
    public Tsid Id { get; private set; } = TsidId.NewId();
    public Tsid TenantId { get; private set; }
    public Tsid ActorAccountId { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string ActorEmail { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Target { get; private set; } = string.Empty;
    public string Details { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private AuditEvent()
    {
    }

    public static AuditEvent Create(Tsid tenantId, Tsid actorAccountId, string actor, string actorEmail, string action, string target, string details, DateTimeOffset nowUtc)
    {
        return new AuditEvent
        {
            Id = TsidId.NewId(),
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
