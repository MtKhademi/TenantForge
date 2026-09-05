using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.TenantMembers;

internal static class TenantMembersFeature
{
    public static IEndpointRouteBuilder MapTenantMembersFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/members", async (
            string tenantId,
            ClaimsPrincipal principal,
            IamDbContext db) =>
        {
            var accountId = GetAuthenticatedAccountId(principal);
            if (accountId is null)
            {
                return Results.Forbid();
            }

            if (!Guid.TryParse(tenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
            {
                return Results.Forbid();
            }

            var hasTenantMembership = await db.TenantMemberships
                .AsNoTracking()
                .AnyAsync(membership =>
                    membership.TenantId == tenantGuid
                    && membership.AccountId == accountId.Value);

            if (!hasTenantMembership)
            {
                return Results.Forbid();
            }

            var tenant = await db.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantGuid && tenant.Status == TenantStatus.Active)
                .Select(tenant => new TenantContextResponse(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.Status.ToString()))
                .SingleOrDefaultAsync();

            if (tenant is null)
            {
                return Results.Forbid();
            }

            var memberRows = await db.TenantMemberships
                .AsNoTracking()
                .Where(membership => membership.TenantId == tenantGuid)
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
                .ThenBy(member => member.Id)
                .ToListAsync();

            var members = memberRows.Select(member => new TenantMemberResponse(
                member.Id,
                member.UserId,
                member.Email,
                member.DisplayName,
                member.Role.ToString(),
                member.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new TenantMembersResponse(tenant, members));
        })
        .RequireAuthorization();

        return endpoints;
    }

    private static Guid? GetAuthenticatedAccountId(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var subject = principal.FindFirstValue("sub");
        return Guid.TryParse(subject, out var accountId) && accountId != Guid.Empty
            ? accountId
            : null;
    }
}

internal sealed record TenantMembersResponse(
    TenantContextResponse Tenant,
    IReadOnlyList<TenantMemberResponse> Members);

internal sealed record TenantContextResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status);

internal sealed record TenantMemberResponse(
    Guid Id,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAtUtc);
