namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record AuditListResponse(IReadOnlyList<AuditEventResponse> Events, PaginationMetadata Pagination);
