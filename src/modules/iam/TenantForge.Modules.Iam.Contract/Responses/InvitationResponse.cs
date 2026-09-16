namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record InvitationResponse(
    string Id,
    string Email,
    string Role,
    string Status,
    string ExpiresAtUtc,
    string CreatedAtUtc);
