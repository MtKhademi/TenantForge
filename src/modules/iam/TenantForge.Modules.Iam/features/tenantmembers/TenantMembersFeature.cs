using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Contract.Responses;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Infrastructure;
using TSID.Creator.NET;

namespace TenantForge.Modules.Iam.Features.TenantMembers;

internal static class TenantMembersFeature
{
    public static IEndpointRouteBuilder MapTenantMembersFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/members", async (
            string tenantId,
            HttpRequest request,
            ClaimsPrincipal principal,
            IamDbContext db) =>
        {
            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var accountId = GetAuthenticatedAccountId(principal);
            if (accountId is null)
            {
                return Results.Forbid();
            }

            // B017/S19: the tenant route value must be a canonical TSID string.
            // A malformed, GUID-shaped or decimal value is not a known tenant, so
            // the endpoint fails closed with 403 (the same answer it always gave
            // for an unknown tenant id) rather than throwing.
            if (!TsidId.TryParse(tenantId, out var tenantTsid))
            {
                return Results.Forbid();
            }

            var hasTenantMembership = await db.TenantMemberships
                .AsNoTracking()
                .AnyAsync(membership =>
                    membership.TenantId == tenantTsid
                    && membership.AccountId == accountId.Value);

            if (!hasTenantMembership)
            {
                return Results.Forbid();
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantTsid && tenant.Status == TenantStatus.Active)
                .Select(tenant => new TenantContextResponse(
                    TsidId.Format(tenant.Id),
                    tenant.Name,
                    tenant.Slug,
                    tenant.Status.ToString()))
                .SingleOrDefaultAsync();

            if (tenant is null)
            {
                return Results.Forbid();
            }

            var query = db.TenantMemberships
                .AsNoTracking()
                .Where(membership => membership.TenantId == tenantTsid)
                .Join(
                    db.Accounts.AsNoTracking(),
                    membership => membership.AccountId,
                    account => account.Id,
                    (membership, account) => new
                    {
                        membership.Id,
                        UserId = account.Id,
                        account.Email,
                        account.DisplayName,
                        membership.Role,
                        membership.CreatedAtUtc
                    })
                .OrderBy(member => member.DisplayName)
                .ThenBy(member => member.Email)
                .ThenBy(member => member.Id);

            var (memberRows, pagination) = await PaginationSupport.PageAsync(query, page);

            var members = memberRows.Select(member => new TenantMemberResponse(
                TsidId.Format(member.Id),
                TsidId.Format(member.UserId),
                member.Email,
                member.DisplayName,
                member.Role.ToString(),
                member.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new TenantMembersResponse(tenant, members, pagination));
        })
        .RequireAuthorization();

        return endpoints;
    }

    private static Tsid? GetAuthenticatedAccountId(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        // B017/S19: subject must be a canonical TSID string; a legacy
        // GUID-subject token is denied (403 here, the endpoint's fail-closed
        // answer for an authenticated-but-unusable caller).
        return TsidId.TryParseNullable(principal.FindFirstValue("sub"));
    }
}
