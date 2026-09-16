namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantContextResponse(
    string Id,
    string Name,
    string Slug,
    string Status);
