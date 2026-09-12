using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Features.Roles;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Audit;

internal static class AuditFeature
{
    private const int FirstPageSize = 50;
    private static readonly HashSet<string> KnownActions =
    [
        "Invitation.Created",
        "Role.Created",
        "Role.Updated",
        "Role.Assigned",
        "Role.Unassigned"
    ];

    public static IEndpointRouteBuilder MapAuditFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/audit", async (string tenantId, string? action, string? fromUtc, System.Security.Claims.ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.AuditViewPermission);
            if (auth.Result is not null) return auth.Result;

            if (!string.IsNullOrWhiteSpace(action) && !KnownActions.Contains(action))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["action"] = ["Select a known audit action."] });
            }

            DateTimeOffset? from = null;
            if (!string.IsNullOrWhiteSpace(fromUtc))
            {
                if (!DateTimeOffset.TryParse(fromUtc, out var parsed))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["fromUtc"] = ["Enter a valid UTC timestamp."] });
                }

                from = parsed.ToUniversalTime();
            }

            var query = db.AuditEvents.AsNoTracking().Where(evt => evt.TenantId == auth.TenantId);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(evt => evt.Action == action);
            if (from is not null) query = query.Where(evt => evt.CreatedAtUtc >= from.Value);

            var eventRows = await query
                .OrderByDescending(evt => evt.CreatedAtUtc)
                .ThenByDescending(evt => evt.Id)
                .Take(FirstPageSize)
                .ToListAsync();
            var events = eventRows
                .Select(evt => new AuditEventResponse(evt.Id, evt.Actor, evt.ActorEmail, evt.Action, evt.Target, evt.Details, evt.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new AuditListResponse(events));
        }).RequireAuthorization();

        return endpoints;
    }
}

internal sealed record AuditListResponse(IReadOnlyList<AuditEventResponse> Events);
internal sealed record AuditEventResponse(Guid Id, string Actor, string ActorEmail, string Action, string Target, string Details, string CreatedAtUtc);
