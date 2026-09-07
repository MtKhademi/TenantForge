using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Account;

/// <summary>
/// S11 tenant discovery: answers "which tenants do I belong to?" for the
/// authenticated caller. F018 uses this to populate the tenant switcher
/// without ever touching the admin-only platform tenant list. See
/// docs/design/s11-platform-tenant-boundaries.md for the frozen contract.
/// </summary>
internal static class TenantDiscoveryFeature
{
    public static IEndpointRouteBuilder MapTenantDiscoveryFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/me/tenants", async (ClaimsPrincipal principal, IamDbContext db) =>
        {
            // .RequireAuthorization() already rejected a missing/invalid JWT
            // with 401, so principal.Identity is authenticated here. But a
            // stateless token can outlive row state, so "sub" is verified
            // against the persisted account before any data is read (fail
            // closed, default deny).
            var accountId = GetAuthenticatedAccountId(principal);
            if (accountId is null)
            {
                return Results.Forbid();
            }

            var accountIsUsable = await db.Accounts
                .AsNoTracking()
                .AnyAsync(account => account.Id == accountId.Value && account.Status == AccountStatus.Active);

            if (!accountIsUsable)
            {
                return Results.Forbid();
            }

            // Only active tenants with a membership row for this caller are
            // returned. A suspended tenant is excluded (not returned with a
            // "Suspended" status), and a tenant the caller has left has no
            // membership row at all, so it is naturally excluded. Ordered by
            // name then id for a stable, duplicate-free result (the unique
            // (tenantId, accountId) index already guarantees no duplicates).
            var tenantRows = await db.TenantMemberships
                .AsNoTracking()
                .Where(membership => membership.AccountId == accountId.Value)
                .Join(
                    db.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active),
                    membership => membership.TenantId,
                    tenant => tenant.Id,
                    (membership, tenant) => new
                    {
                        tenant.Id,
                        tenant.Name,
                        tenant.Slug,
                        tenant.Status,
                        membership.Role
                    })
                .OrderBy(row => row.Name)
                .ThenBy(row => row.Id)
                .ToListAsync();

            var tenants = tenantRows
                .Select(row => new DiscoveredTenantResponse(
                    row.Id,
                    row.Name,
                    row.Slug,
                    row.Status.ToString(),
                    row.Role.ToString()))
                .ToList();

            return Results.Ok(new TenantDiscoveryResponse(tenants));
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

internal sealed record TenantDiscoveryResponse(IReadOnlyList<DiscoveredTenantResponse> Tenants);

internal sealed record DiscoveredTenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string MembershipRole);
