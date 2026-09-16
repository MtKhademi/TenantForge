namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record CreateInvitationRequest(string? Email, string? Role);
