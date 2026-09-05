using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Dashboard;

internal static class DashboardSummaryFeature
{
    // Honest values for the current stage. The API is "healthy" when this
    // endpoint answers at all (no health infrastructure probes dependencies
    // yet). The platform administrator count is derived from the persisted
    // accounts: the number of active platform administrators.
    private const string ApiStatusHealthy = "Healthy";

    public static IEndpointRouteBuilder MapDashboardSummaryFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/dashboard-summary", async (IHostEnvironment environment, IamDbContext db) =>
        {
            var platformAdminCount = await db.Accounts
                .CountAsync(a => a.IsPlatformAdmin && a.Status == AccountStatus.Active);

            var summary = new DashboardSummaryResponse(
                Environment: environment.EnvironmentName,
                ApiStatus: ApiStatusHealthy,
                PlatformAdminCount: platformAdminCount,
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
