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
    /// B035: gates every mutating category/product endpoint. Granted to a
    /// tenant Owner always, or to a member holding a tenant role whose
    /// permission_keys array contains this key.
    /// </summary>
    internal const string CatalogManagePermission = "Shop.Catalog.Manage";

    /// <summary>
    /// B035: gates every mutating shipping-rate/coupon endpoint. Same
    /// Owner-bypass/assigned-role-key rule as CatalogManagePermission.
    /// </summary>
    internal const string ShippingManagePermission = "Shop.Shipping.Manage";

    /// <summary>
    /// B039: gates the profile/policy save endpoint (PUT
    /// /api/tenants/{tenantId}/shop/profile). Same Owner-bypass/assigned-
    /// role-key rule as the other Shop keys.
    /// </summary>
    internal const string SettingsManagePermission = "Shop.Settings.Manage";

    /// <summary>The three keys Shop owns — see ShopPermissionCatalogContributor.</summary>
    internal static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        CatalogManagePermission,
        ShippingManagePermission,
        SettingsManagePermission
    };

    /// <summary>
    /// Membership-only overload — every read-only Shop endpoint keeps
    /// calling exactly this, unchanged from before this task.
    /// </summary>
    internal static Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db) =>
        AuthorizeTenantAccessAsync(tenantId, principal, db, null);

    /// <summary>
    /// Permission-checking overload. Every mutating Shop endpoint (Scope
    /// table in this Spec) now calls this with CatalogManagePermission,
    /// ShippingManagePermission or SettingsManagePermission.
    ///
    /// (B035 delivery note: the Spec sketched this as a non-nullable
    /// internal shim plus a private nullable core, but C# forbids two
    /// overloads whose only difference is a reference-type nullable
    /// annotation (CS0111) — the same adaptation B034 made to
    /// RolesFeature.AuthorizeTenantAccessAsync. The nullable-annotated
    /// parameter is therefore the single implementation; the 3-arg
    /// membership overload delegates to it with null and never dereferences
    /// a key.)
    /// </summary>
    internal static async Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db,
        string? permissionKey)
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

        // Same active/active/active join RolesFeature.AuthorizeTenantAccessAsync
        // uses, now selecting the membership's role string instead of a bare
        // count, so a permission check can tell Owner apart from Member
        // without a second round trip.
        var roles = await db.Database.SqlQueryRaw<string>(
            """
            SELECT m.role AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_accounts a ON a.id = m.account_id AND a.status = 'Active'
            JOIN iam_tenants t ON t.id = m.tenant_id AND t.status = 'Active'
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantTsid.ToLong(),
            accountTsid.Value.ToLong())
            .ToListAsync();

        if (roles.Count == 0)
        {
            return ShopTenantAccess.Forbidden;
        }

        if (permissionKey is not null)
        {
            var granted = roles[0] == "Owner"
                ? KnownKeys.Contains(permissionKey)
                : (await ResolveAssignedShopKeysAsync(db, tenantTsid, accountTsid.Value)).Contains(permissionKey);
            if (!granted)
            {
                return ShopTenantAccess.Forbidden;
            }
        }

        return new ShopTenantAccess(tenantTsid, accountTsid.Value, null);
    }

    /// <summary>
    /// Every Shop permission key the caller holds through an assigned
    /// tenant role, intersected with Shop's own KnownKeys (a role's
    /// permission_keys array may also hold IAM keys, which are irrelevant
    /// here). Raw SQL for the same cross-database reason as the membership
    /// check above: iam_tenant_member_role_assignments and iam_tenant_roles
    /// are IAM's own tables, unreachable from ShopDbContext as EF entities.
    /// </summary>
    private static async Task<HashSet<string>> ResolveAssignedShopKeysAsync(ShopDbContext db, Tsid tenantId, Tsid accountId)
    {
        var keys = await db.Database.SqlQueryRaw<string>(
            """
            SELECT DISTINCT unnest(r.permission_keys) AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_tenant_member_role_assignments asg ON asg.tenant_membership_id = m.id
            JOIN iam_tenant_roles r ON r.id = asg.tenant_role_id
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantId.ToLong(),
            accountId.ToLong())
            .ToListAsync();

        return keys.Where(KnownKeys.Contains).ToHashSet(StringComparer.Ordinal);
    }
}
