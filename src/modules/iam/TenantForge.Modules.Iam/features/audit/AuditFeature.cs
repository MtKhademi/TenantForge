using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Features.Roles;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Audit;

internal static class AuditFeature
{
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
        endpoints.MapGet("/api/tenants/{tenantId}/audit", async (string tenantId, string? action, string? fromUtc, HttpRequest request, System.Security.Claims.ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.AuditViewPermission);
            if (auth.Result is not null) return auth.Result;
            if (!PaginationSupport.TryBind(request, out var page, out var paginationErrors))
            {
                return Results.ValidationProblem(paginationErrors);
            }

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

            var orderedQuery = query
                .OrderByDescending(evt => evt.CreatedAtUtc)
                .ThenByDescending(evt => evt.Id);
            var (eventRows, pagination) = await PaginationSupport.PageAsync(orderedQuery, page);
            var events = eventRows
                .Select(evt => new AuditEventResponse(TsidId.Format(evt.Id), evt.Actor, evt.ActorEmail, evt.Action, evt.Target, evt.Details, evt.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new AuditListResponse(events, pagination));
        }).RequireAuthorization();

        return endpoints;
    }
}

internal sealed record AuditListResponse(IReadOnlyList<AuditEventResponse> Events, PaginationMetadata Pagination);
internal sealed record AuditEventResponse(string Id, string Actor, string ActorEmail, string Action, string Target, string Details, string CreatedAtUtc);
