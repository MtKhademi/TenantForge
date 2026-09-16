namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record InvitationListResponse(IReadOnlyList<InvitationResponse> Invitations, PaginationMetadata Pagination);
