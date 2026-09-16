namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record TenantSummaryResponse(
    string Id,
    string Name,
    string Slug,
    string Status,
    int MemberCount,
    string CreatedAtUtc);
