namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record DiscoveredTenantResponse(
    string Id,
    string Name,
    string Slug,
    string Status,
    string MembershipRole);
