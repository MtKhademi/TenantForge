namespace TenantForge.Modules.Iam.Features.Dashboard;

public record DashboardSummaryResponse(
    string Environment,
    string ApiStatus,
    int PlatformAdminCount,
    string GeneratedAtUtc);
