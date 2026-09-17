using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Authorization;

/// <summary>
/// The result of checking whether the caller may act on a tenant's Shop
/// data. Mirrors the shape of TenantForge.Modules.Iam.Features.Roles.TenantAccess
/// (a Result slot the caller returns directly when non-null).
/// </summary>
internal sealed record ShopTenantAccess(Tsid TenantId, Tsid AccountId, IResult? Result)
{
    public static ShopTenantAccess Forbidden { get; } = new(default, default, Results.Forbid());
}

internal static class ShopAuthorization
{
    /// <summary>
    /// Parses the route's tenantId, reads the caller's account id from the
    /// JWT "sub" claim, then checks IAM's own iam_tenant_memberships table
    /// with a raw SQL query — see B026's Spec Context for why this cannot be
    /// an EF entity/DbSet reference. Requires an active membership for an
    /// active account in an active tenant, mirroring
    /// RolesFeature.AuthorizeTenantAccessAsync's own active/active/active
    /// join exactly.
    /// </summary>
    public static async Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return ShopTenantAccess.Forbidden;
        }

        if (!TsidId.TryParse(tenantId, out var tenantTsid))
        {
            return ShopTenantAccess.Forbidden;
        }

        var accountTsid = TsidId.TryParseNullable(principal.FindFirstValue("sub"));
        if (accountTsid is null)
        {
            return ShopTenantAccess.Forbidden;
        }

        var membershipCount = await db.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*)::int AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_accounts a ON a.id = m.account_id AND a.status = 'Active'
            JOIN iam_tenants t ON t.id = m.tenant_id AND t.status = 'Active'
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantTsid.ToLong(),
            accountTsid.Value.ToLong())
            .SingleAsync();

        if (membershipCount == 0)
        {
            return ShopTenantAccess.Forbidden;
        }

        return new ShopTenantAccess(tenantTsid, accountTsid.Value, null);
    }
}
