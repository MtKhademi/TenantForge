namespace TenantForge.Modules.Iam.Contract.Requests;

public sealed record CreateTenantRequest(string? Name, string? Slug, string? OwnerUserId);
