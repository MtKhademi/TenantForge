namespace TenantForge.Modules.Iam.Contract.Responses;

public sealed record DashboardSummaryResponse(
    string Environment,
    string ApiStatus,
    int PlatformAdminCount,
    string GeneratedAtUtc);
