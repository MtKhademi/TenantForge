using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Iam;

namespace TenantForge.Modules.Iam.Features.Dashboard;

internal static class DashboardSummaryFeature
{
    // Honest values for the current stage. The API is "healthy" when this endpoint
    // answers at all (no health infrastructure probes dependencies yet), and the
    // platform administrator count is the single seeded development administrator
    // from B001 — no persistence exists yet, so there is nothing to count.
    // B004/B005 will derive this from stored users instead of a constant.
    private const string ApiStatusHealthy = "Healthy";
    private const int PlatformAdminCountAtThisStage = 1;

    public static IEndpointRouteBuilder MapDashboardSummaryFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/dashboard-summary", (IHostEnvironment environment) =>
        {
            var summary = new DashboardSummaryResponse(
                Environment: environment.EnvironmentName,
                ApiStatus: ApiStatusHealthy,
                PlatformAdminCount: PlatformAdminCountAtThisStage,
                GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"));

            return Results.Ok(summary);
        })
        // A named claim policy (registered in IAMConfig) does the authorization:
        // unauthenticated -> 401 (challenge), authenticated but missing/incorrect
        // isPlatformAdmin claim -> 403 (forbid). No manual claim re-check here, so
        // the 401 vs 403 distinction is preserved rather than collapsed to 401.
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        return endpoints;
    }
}
