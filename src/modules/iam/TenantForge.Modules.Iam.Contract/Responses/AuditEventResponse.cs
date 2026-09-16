namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record AuditEventResponse(
    string Id,
    string Actor,
    string ActorEmail,
    string Action,
    string Target,
    string Details,
    string CreatedAtUtc);
