---
id: B034
slice: S31
title: Shared permission catalog contract (BuildingBlocks) and IAM migration
agent: backend-mentor
source: tasks/slices/031-cross-module-permissions-and-nav.md
---

# Objective

Add a small, shared permission-catalog contract to
`TenantForge.BuildingBlocks`, and migrate IAM's existing hardcoded
permission catalog onto it with **zero visible behavior change**. This
task adds no Shop permission key and changes no Shop endpoint — it only
builds the seam B035 plugs into next.

This Spec gives you every file's exact path and exact full code. Follow
it literally.

# Context

Read `tasks/slices/031-cross-module-permissions-and-nav.md` completely —
it documents exactly why this is a real BuildingBlocks admission (not a
premature one) and a real gap this task corrects beyond what was first
proposed (`ResolvePermissionsAsync`'s own filtering, and two files
outside `RolesFeature.cs`).

Read the full, current
`src/modules/iam/TenantForge.Modules.Iam/features/roles/RolesFeature.cs`
(425 lines) before changing it — this Spec's diffs below are written
against that exact file; if the delivered file has drifted since this
Spec was written, adapt the same changes to the real current code and
say so.

Read `docs/building-blocks/README.md` completely before editing it —
Section 1 (purpose/non-purpose), Section 7 (admission checklist) and
Section 8 (explicit exclusions, which today has a row named "Permission
catalog and tenant authorization" excluding `RolesFeature`/
`AuthorizationPolicyNames` — this task amends that row, it does not
delete IAM's own ownership of its endpoints/authorization rules, only
the shared *shape* of a permission group/descriptor and the
contributor/aggregator seam).

# Scope — every file, in order

## 1. New BuildingBlocks types

**`src/building-blocks/TenantForge.BuildingBlocks/Permissions/PermissionDescriptor.cs`:**

```csharp
namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// One permission key a module owns, with its display label, description
/// and kind ("read"/"write"). Mirrors
/// TenantForge.Modules.Iam.Contract.Responses.PermissionResponse
/// field-for-field — that type stays IAM's own HTTP wire shape; this one
/// is the cross-module in-memory shape every contributor returns.
/// </summary>
public sealed record PermissionDescriptor(string Key, string Label, string Description, string Kind);
```

**`src/building-blocks/TenantForge.BuildingBlocks/Permissions/PermissionGroup.cs`:**

```csharp
namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// One named group of permissions a module owns. Mirrors
/// TenantForge.Modules.Iam.Contract.Responses.PermissionGroupResponse
/// field-for-field, for the same reason as PermissionDescriptor.
/// </summary>
public sealed record PermissionGroup(string Id, string Label, string Description, IReadOnlyList<PermissionDescriptor> Permissions);
```

**`src/building-blocks/TenantForge.BuildingBlocks/Permissions/IPermissionCatalogContributor.cs`:**

```csharp
namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// Implemented once per module that owns permission keys. Registered as
/// `IPermissionCatalogContributor` in the module's own `RegisterServices`
/// (see IAMConfig.cs and, in B035, ShopConfig.cs) so the API host can
/// discover every contributor without any module referencing another.
/// </summary>
public interface IPermissionCatalogContributor
{
    IReadOnlyList<PermissionGroup> GetPermissionGroups();
}
```

**`src/building-blocks/TenantForge.BuildingBlocks/Permissions/IAggregatedPermissionCatalog.cs`:**

```csharp
namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// The union of every registered IPermissionCatalogContributor's groups
/// and known keys, computed once at startup (see AggregatedPermissionCatalog).
/// </summary>
public interface IAggregatedPermissionCatalog
{
    IReadOnlyList<PermissionGroup> AllGroups { get; }
    IReadOnlySet<string> AllKnownKeys { get; }
}
```

**`src/building-blocks/TenantForge.BuildingBlocks/Permissions/AggregatedPermissionCatalog.cs`:**

```csharp
namespace TenantForge.BuildingBlocks.Permissions;

/// <summary>
/// The one, trivial implementation of IAggregatedPermissionCatalog.
/// Flattens every registered contributor's groups and keys exactly once,
/// in the constructor — module contributors are fixed at startup
/// (registered as singletons), so there is no per-call recomputation.
/// </summary>
public sealed class AggregatedPermissionCatalog : IAggregatedPermissionCatalog
{
    public IReadOnlyList<PermissionGroup> AllGroups { get; }
    public IReadOnlySet<string> AllKnownKeys { get; }

    public AggregatedPermissionCatalog(IEnumerable<IPermissionCatalogContributor> contributors)
    {
        var groups = new List<PermissionGroup>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            foreach (var group in contributor.GetPermissionGroups())
            {
                groups.Add(group);
                foreach (var permission in group.Permissions)
                {
                    keys.Add(permission.Key);
                }
            }
        }

        AllGroups = groups;
        AllKnownKeys = keys;
    }
}
```

No new `.csproj` entry is needed — these are plain `.cs` files under the
existing `TenantForge.BuildingBlocks` project, which already compiles
every `.cs` file under its directory.

## 2. `src/modules/iam/TenantForge.Modules.Iam/features/roles/IamPermissionCatalogContributor.cs` (new file)

The exact same 3 groups `RolesFeature.CatalogGroups` currently returns
(same ids, labels, descriptions, keys, kinds), translated into the new
BuildingBlocks shape:

```csharp
using TenantForge.BuildingBlocks.Permissions;

namespace TenantForge.Modules.Iam.Features.Roles;

/// <summary>
/// IAM's own contribution to the shared permission catalog (B034). The
/// exact same 3 groups RolesFeature used to hardcode as
/// Contract-typed PermissionGroupResponse values — same ids, labels,
/// descriptions, keys, kinds — now expressed in the cross-module shape.
/// </summary>
internal sealed class IamPermissionCatalogContributor : IPermissionCatalogContributor
{
    public IReadOnlyList<PermissionGroup> GetPermissionGroups() =>
    [
        new("roles", "نقش‌ها", "مدیریت نقش‌ها و مجوزهای مستأجر.",
        [
            new(RolesFeature.RolesManagePermission, "مدیریت نقش‌ها", "اجازه ایجاد، ویرایش و تخصیص نقش‌های مستأجر.", "write")
        ]),
        new("invitations", "دعوت‌ها", "مدیریت دعوت‌نامه‌های مستأجر.",
        [
            new(RolesFeature.InvitationsViewPermission, "مشاهده دعوت‌ها", "اجازه دیدن دعوت‌نامه‌های در انتظار.", "read"),
            new(RolesFeature.InvitationsCreatePermission, "ایجاد دعوت", "اجازه ایجاد دعوت‌نامه جدید برای مستأجر.", "write")
        ]),
        new("audit", "گزارش فعالیت", "دسترسی به رویدادهای ثبت‌شده مستأجر.",
        [
            new(RolesFeature.AuditViewPermission, "مشاهده گزارش فعالیت", "اجازه خواندن گزارش فعالیت مستأجر.", "read")
        ])
    ];
}
```

## 3. Rewrite `RolesFeature.cs`

Replace the entire file with the version below. Every behavioral change
is confined to: (a) the catalog now comes from the injected aggregate,
mapped to the Contract wire shape at the endpoint; (b) `ValidateRoleRequest`
and `ResolvePermissionsAsync` now check against `catalog.AllKnownKeys`
instead of the module-private `PermissionKeys` set; (c) every call site
that needs a permission check now also passes the injected catalog. The
record types at the bottom of the file, `HasAnyEffectiveRoleAdministratorAsync`,
`BeginTenantMutationAsync`, `ParseTenantId`, `GetAuthenticatedAccountId`
and `IsUniqueConstraintViolation`/`DuplicateRoleProblem` are byte-for-byte
unchanged from the current file — only reproduced here because the whole
file is being replaced.

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Iam.Contract.Queries;
using TenantForge.Modules.Iam.Contract.Requests;
using TenantForge.Modules.Iam.Contract.Responses;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Infrastructure;
using TSID.Creator.NET;

namespace TenantForge.Modules.Iam.Features.Roles;

internal static class RolesFeature
{
    internal const string RolesManagePermission = "IAM.Roles.Manage";
    internal const string InvitationsViewPermission = "IAM.Invitations.View";
    internal const string InvitationsCreatePermission = "IAM.Invitations.Create";
    internal const string AuditViewPermission = "IAM.Audit.View";

    public static IEndpointRouteBuilder MapRolesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/permissions/catalog", (IAggregatedPermissionCatalog catalog) =>
            Results.Ok(new PermissionCatalogResponse(ToResponseGroups(catalog.AllGroups))))
            .RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/roles", async (string tenantId, HttpRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;
            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var (roles, pagination) = await BuildPagedRoleResponsesAsync(db, access.TenantId, page);
            return Results.Ok(new PagedTenantRolesResponse(roles, pagination));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/roles", async (string tenantId, CreateRoleRequest request, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesManagePermission);
            if (access.Result is not null) return access.Result;

            var errors = ValidateRoleRequest(request.Name, request.PermissionKeys, requireName: true, catalog);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var now = DateTimeOffset.UtcNow;
            await using var transaction = await BeginTenantMutationAsync(db, access.TenantId);
            var role = TenantRole.Create(access.TenantId, request.Name!, request.PermissionKeys ?? [], now);
            db.TenantRoles.Add(role);
            db.AuditEvents.Add(AuditEvent.Create(access.TenantId, access.AccountId, access.Actor, access.ActorEmail, "Role.Created", role.Name, $"نقش {role.Name} ایجاد شد.", now));

            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return DuplicateRoleProblem();
            }

            var response = (await BuildRoleResponsesAsync(db, access.TenantId)).Single(item => item.Id == TsidId.Format(role.Id));
            return Results.Created($"/api/tenants/{access.TenantId}/roles/{role.Id}", response);
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/roles/{roleId}", async (string tenantId, string roleId, UpdateRoleRequest request, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var tenantTsid = ParseTenantId(tenantId);
            var roleTsid = ParseTenantId(roleId);
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesManagePermission);
            if (tenantTsid is null || roleTsid is null || access.Result is not null)
            {
                return access.Result ?? Results.Forbid();
            }

            var errors = ValidateRoleRequest(null, request.PermissionKeys, requireName: false, catalog);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            await using var transaction = await BeginTenantMutationAsync(db, access.TenantId);
            var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.TenantId == access.TenantId && item.Id == roleTsid.Value);
            if (role is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound();
            }

            if (role.Kind != "custom")
            {
                await transaction.RollbackAsync();
                return Results.Conflict();
            }

            if (!await HasAnyEffectiveRoleAdministratorAsync(db, access.TenantId, updatedRoleId: role.Id, updatedPermissionKeys: request.PermissionKeys!, removedAssignment: null))
            {
                await transaction.RollbackAsync();
                return Results.Conflict();
            }

            var now = DateTimeOffset.UtcNow;
            role.ReplacePermissions(request.PermissionKeys!, now);
            db.AuditEvents.Add(AuditEvent.Create(access.TenantId, access.AccountId, access.Actor, access.ActorEmail, "Role.Updated", role.Name, $"مجوزهای نقش {role.Name} به‌روزرسانی شد.", now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok((await BuildRoleResponsesAsync(db, access.TenantId)).Single(item => item.Id == TsidId.Format(role.Id)));
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db, catalog);
            if (parsed.Result is not null) return parsed.Result;

            await using var transaction = await BeginTenantMutationAsync(db, parsed.TenantId);
            var exists = await db.TenantMemberRoleAssignments.AnyAsync(a => a.TenantMembershipId == parsed.MemberId && a.TenantRoleId == parsed.RoleId);
            if (!exists)
            {
                var now = DateTimeOffset.UtcNow;
                db.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(parsed.MemberId, parsed.RoleId, now));
                var role = await db.TenantRoles.AsNoTracking().SingleAsync(r => r.Id == parsed.RoleId);
                db.AuditEvents.Add(AuditEvent.Create(parsed.TenantId, parsed.AccountId, parsed.Actor, parsed.ActorEmail, "Role.Assigned", role.Name, $"نقش {role.Name} به عضو مستأجر اختصاص یافت.", now));
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
        }).RequireAuthorization();

        endpoints.MapDelete("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db, catalog);
            if (parsed.Result is not null) return parsed.Result;

            await using var transaction = await BeginTenantMutationAsync(db, parsed.TenantId);
            var assignment = await db.TenantMemberRoleAssignments.SingleOrDefaultAsync(a => a.TenantMembershipId == parsed.MemberId && a.TenantRoleId == parsed.RoleId);
            if (assignment is null)
            {
                await transaction.CommitAsync();
                return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
            }

            if (!await HasAnyEffectiveRoleAdministratorAsync(db, parsed.TenantId, updatedRoleId: null, updatedPermissionKeys: null, removedAssignment: new(parsed.MemberId, parsed.RoleId)))
            {
                await transaction.RollbackAsync();
                return Results.Conflict();
            }

            var role = await db.TenantRoles.SingleAsync(r => r.Id == parsed.RoleId);
            db.TenantMemberRoleAssignments.Remove(assignment);
            db.AuditEvents.Add(AuditEvent.Create(parsed.TenantId, parsed.AccountId, parsed.Actor, parsed.ActorEmail, "Role.Unassigned", role.Name, $"نقش {role.Name} از عضو مستأجر حذف شد.", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/me/permissions", async (string tenantId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var permissions = await ResolvePermissionsAsync(db, access.TenantId, access.AccountId, catalog);
            return Results.Ok(new ResolvedPermissionsResponse(permissions));
        }).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// Membership-only overload — unchanged call sites all across IAM keep
    /// calling exactly this, with no catalog involved.
    /// </summary>
    internal static Task<TenantAccess> AuthorizeTenantAccessAsync(string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        AuthorizeTenantAccessAsync(tenantId, principal, db, null, null);

    /// <summary>
    /// Permission-checking overload. Every caller that used to pass a bare
    /// permissionKey now also passes the injected aggregate catalog, so the
    /// caller's assigned/owner keys can be resolved against every module's
    /// known keys, not just IAM's own.
    /// </summary>
    internal static Task<TenantAccess> AuthorizeTenantAccessAsync(string tenantId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog, string permissionKey) =>
        AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, (string?)permissionKey);

    private static async Task<TenantAccess> AuthorizeTenantAccessAsync(string tenantId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog? catalog, string? permissionKey)
    {
        var tenantTsid = ParseTenantId(tenantId);
        var accountId = GetAuthenticatedAccountId(principal);
        if (tenantTsid is null || accountId is null)
        {
            return TenantAccess.Forbidden;
        }

        var row = await db.TenantMemberships.AsNoTracking()
            .Where(member => member.TenantId == tenantTsid.Value && member.AccountId == accountId.Value)
            .Join(db.Accounts.AsNoTracking().Where(account => account.Status == AccountStatus.Active), member => member.AccountId, account => account.Id, (member, account) => new { Membership = member, Account = account })
            .Join(db.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active), row => row.Membership.TenantId, tenant => tenant.Id, (row, tenant) => row)
            .SingleOrDefaultAsync();

        if (row is null)
        {
            return TenantAccess.Forbidden;
        }

        if (permissionKey is not null)
        {
            var permissions = await ResolvePermissionsAsync(db, tenantTsid.Value, accountId.Value, catalog!);
            if (!permissions.Contains(permissionKey, StringComparer.Ordinal))
            {
                return TenantAccess.Forbidden;
            }
        }

        return new(tenantTsid.Value, accountId.Value, row.Membership.Id, row.Membership.Role, row.Account.DisplayName, row.Account.Email, null);
    }

    internal static async Task<bool> HasPermissionAsync(IamDbContext db, Tsid tenantId, Tsid accountId, string permissionKey, IAggregatedPermissionCatalog catalog) =>
        (await ResolvePermissionsAsync(db, tenantId, accountId, catalog)).Contains(permissionKey, StringComparer.Ordinal);

    private static async Task<AssignmentValidation> ValidateAssignmentAsync(string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog)
    {
        var memberTsid = ParseTenantId(memberId);
        var roleTsid = ParseTenantId(roleId);
        var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesManagePermission);
        if (memberTsid is null || roleTsid is null || access.Result is not null)
        {
            return AssignmentValidation.Forbidden;
        }

        var memberExists = await db.TenantMemberships.AnyAsync(member => member.TenantId == access.TenantId && member.Id == memberTsid.Value);
        var roleExists = await db.TenantRoles.AnyAsync(role => role.TenantId == access.TenantId && role.Id == roleTsid.Value);
        if (!memberExists || !roleExists)
        {
            return new(access.TenantId, memberTsid.Value, roleTsid.Value, access.AccountId, access.Actor, access.ActorEmail, Results.NotFound());
        }

        return new(access.TenantId, memberTsid.Value, roleTsid.Value, access.AccountId, access.Actor, access.ActorEmail, null);
    }

    private static async Task<IReadOnlyList<TenantRoleResponse>> BuildRoleResponsesAsync(IamDbContext db, Tsid tenantId)
    {
        var roles = await db.TenantRoles.AsNoTracking().Where(role => role.TenantId == tenantId).OrderBy(role => role.Name).ThenBy(role => role.Id).ToListAsync();
        return await BuildRoleResponsesAsync(db, tenantId, roles);
    }

    private static async Task<(IReadOnlyList<TenantRoleResponse> Roles, PaginationMetadata Pagination)> BuildPagedRoleResponsesAsync(IamDbContext db, Tsid tenantId, PaginationQuery page)
    {
        var query = db.TenantRoles.AsNoTracking()
            .Where(role => role.TenantId == tenantId)
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Id);
        var totalCount = await query.CountAsync();
        var roles = await query.Skip(page.Offset).Take(page.PageSize).ToListAsync();
        return (await BuildRoleResponsesAsync(db, tenantId, roles), PaginationMetadata.From(page, totalCount));
    }

    private static async Task<IReadOnlyList<TenantRoleResponse>> BuildRoleResponsesAsync(IamDbContext db, Tsid tenantId, IReadOnlyList<TenantRole> roles)
    {
        var roleIds = roles.Select(role => role.Id).ToHashSet();
        var assignments = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Join(db.TenantMemberships.AsNoTracking().Where(member => member.TenantId == tenantId), assignment => assignment.TenantMembershipId, member => member.Id, (assignment, member) => new { assignment.TenantRoleId, MemberId = member.Id })
            .Where(assignment => roleIds.Contains(assignment.TenantRoleId))
            .ToListAsync();

        return roles.Select(role => new TenantRoleResponse(
            TsidId.Format(role.Id),
            role.Name,
            role.Description,
            role.Kind,
            role.PermissionKeys,
            assignments.Where(a => a.TenantRoleId == role.Id).Select(a => TsidId.Format(a.MemberId)).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            role.CreatedAtUtc.UtcDateTime.ToString("O"),
            role.UpdatedAtUtc.UtcDateTime.ToString("O"))).ToList();
    }

    private static async Task<IReadOnlyList<string>> ResolvePermissionsAsync(IamDbContext db, Tsid tenantId, Tsid accountId, IAggregatedPermissionCatalog catalog)
    {
        var membership = await db.TenantMemberships.AsNoTracking()
            .Where(member => member.TenantId == tenantId && member.AccountId == accountId)
            .Join(db.Accounts.AsNoTracking().Where(account => account.Status == AccountStatus.Active), member => member.AccountId, account => account.Id, (member, _) => member)
            .Join(db.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active), member => member.TenantId, tenant => tenant.Id, (member, _) => member)
            .SingleOrDefaultAsync();
        if (membership is null) return [];

        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (membership.Role == TenantMembershipRole.Owner)
        {
            // Owner bypass now spans every registered module's known keys,
            // not just IAM's own — see this Spec's Context for why this
            // must change together with the line below, not later in B035.
            keys.UnionWith(catalog.AllKnownKeys);
        }

        var assignedKeys = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantMembershipId == membership.Id)
            .Join(db.TenantRoles.AsNoTracking(), assignment => assignment.TenantRoleId, role => role.Id, (_, role) => role.PermissionKeys)
            .ToListAsync();
        foreach (var set in assignedKeys)
        {
            keys.UnionWith(set.Where(catalog.AllKnownKeys.Contains));
        }

        return keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    private static async Task<bool> HasAnyEffectiveRoleAdministratorAsync(IamDbContext db, Tsid tenantId, Tsid? updatedRoleId, IReadOnlyList<string>? updatedPermissionKeys, RemovedAssignment? removedAssignment)
    {
        var activeMembers = await db.TenantMemberships.AsNoTracking()
            .Where(member => member.TenantId == tenantId)
            .Join(db.Accounts.AsNoTracking().Where(account => account.Status == AccountStatus.Active), member => member.AccountId, account => account.Id, (member, _) => member)
            .ToListAsync();

        var administratorAccountIds = activeMembers
            .Where(member => member.Role == TenantMembershipRole.Owner)
            .Select(member => member.AccountId)
            .ToHashSet();

        var activeMembershipIds = activeMembers.Select(member => member.Id).ToHashSet();
        var memberAccountByMembershipId = activeMembers.ToDictionary(member => member.Id, member => member.AccountId);
        var roleKeys = await db.TenantRoles.AsNoTracking()
            .Where(role => role.TenantId == tenantId)
            .Select(role => new { role.Id, role.PermissionKeys })
            .ToDictionaryAsync(role => role.Id, role => (IReadOnlyList<string>)role.PermissionKeys);

        if (updatedRoleId is not null)
        {
            roleKeys[updatedRoleId.Value] = updatedPermissionKeys ?? [];
        }

        var assignments = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Where(assignment => activeMembershipIds.Contains(assignment.TenantMembershipId))
            .ToListAsync();

        foreach (var assignment in assignments)
        {
            if (removedAssignment is not null
                && assignment.TenantMembershipId == removedAssignment.TenantMembershipId
                && assignment.TenantRoleId == removedAssignment.TenantRoleId)
            {
                continue;
            }

            if (roleKeys.TryGetValue(assignment.TenantRoleId, out var keys)
                && keys.Contains(RolesManagePermission, StringComparer.Ordinal))
            {
                administratorAccountIds.Add(memberAccountByMembershipId[assignment.TenantMembershipId]);
            }
        }

        return administratorAccountIds.Count > 0;
    }

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTenantMutationAsync(IamDbContext db, Tsid tenantId)
    {
        var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({TsidId.Format(tenantId)}, 0))");
        return transaction;
    }

    private static Dictionary<string, string[]> ValidateRoleRequest(string? name, IReadOnlyList<string>? permissionKeys, bool requireName, IAggregatedPermissionCatalog catalog)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (requireName)
        {
            var trimmedName = name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedName)) errors["name"] = ["Role name is required."];
            else if (trimmedName.Length > 80) errors["name"] = ["Role name must be 80 characters or fewer."];
        }

        if (permissionKeys is null || permissionKeys.Count == 0)
        {
            errors["permissionKeys"] = ["Select at least one permission."];
        }
        else if (permissionKeys.Any(key => !catalog.AllKnownKeys.Contains(key)))
        {
            errors["permissionKeys"] = ["Select only known permission keys."];
        }

        return errors;
    }

    private static IReadOnlyList<PermissionGroupResponse> ToResponseGroups(IReadOnlyList<PermissionGroup> groups) =>
        groups.Select(group => new PermissionGroupResponse(
            group.Id,
            group.Label,
            group.Description,
            group.Permissions.Select(permission => new PermissionResponse(permission.Key, permission.Label, permission.Description, permission.Kind)).ToList()))
        .ToList();

    private static Tsid? ParseTenantId(string value) => TsidId.TryParseNullable(value);

    private static Tsid? GetAuthenticatedAccountId(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true }) return null;
        // B017/S19: subject must be a canonical TSID string; a legacy
        // GUID-subject token is denied (403 here) instead of crashing.
        return TsidId.TryParseNullable(principal.FindFirstValue("sub"));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult DuplicateRoleProblem() => Results.Problem(title: "Duplicate tenant role", detail: "A role with this name already exists in this tenant.", statusCode: StatusCodes.Status409Conflict);
}

// B023/S23: the nine contract records that lived below moved to
// TenantForge.Modules.Iam.Contract; only the handler-only records remain here.
internal sealed record TenantAccess(Tsid TenantId, Tsid AccountId, Tsid MembershipId, TenantMembershipRole MembershipRole, string Actor, string ActorEmail, IResult? Result)
{
    public bool IsOwner => MembershipRole == TenantMembershipRole.Owner;
    public static TenantAccess Forbidden { get; } = new(default, default, default, TenantMembershipRole.Member, string.Empty, string.Empty, Results.Forbid());
}

internal sealed record ActorSnapshot(string DisplayName, string Email);
internal sealed record AssignmentValidation(Tsid TenantId, Tsid MemberId, Tsid RoleId, Tsid AccountId, string Actor, string ActorEmail, IResult? Result)
{
    public static AssignmentValidation Forbidden { get; } = new(default, default, default, default, string.Empty, string.Empty, Results.Forbid());
}
internal sealed record RemovedAssignment(Tsid TenantMembershipId, Tsid TenantRoleId);
```

Note what did **not** change: `CatalogGroups` and the module-private
`PermissionKeys`/`KnownPermissionKeys` are removed entirely (replaced by
`IamPermissionCatalogContributor` and the injected catalog) —
`KnownPermissionKeys` had no consumer anywhere else in the codebase
(verified with `grep -rn "KnownPermissionKeys" src/ tests/`), so removing
it is safe.

## 4. Retrofit the two other call sites outside `RolesFeature.cs`

**`src/modules/iam/TenantForge.Modules.Iam/features/audit/AuditFeature.cs`** —
its one endpoint currently reads:

```csharp
        endpoints.MapGet("/api/tenants/{tenantId}/audit", async (string tenantId, string? action, string? fromUtc, HttpRequest request, System.Security.Claims.ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.AuditViewPermission);
```

Change the lambda's parameter list to add
`TenantForge.BuildingBlocks.Permissions.IAggregatedPermissionCatalog catalog`
(add `using TenantForge.BuildingBlocks.Permissions;` at the top of the
file instead, and just write `IAggregatedPermissionCatalog catalog`) and
the call to pass it:

```csharp
        endpoints.MapGet("/api/tenants/{tenantId}/audit", async (string tenantId, string? action, string? fromUtc, HttpRequest request, System.Security.Claims.ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesFeature.AuditViewPermission);
```

Nothing else in this file changes.

**`src/modules/iam/TenantForge.Modules.Iam/features/invitations/InvitationsFeature.cs`** —
add `using TenantForge.BuildingBlocks.Permissions;` at the top. Its two
endpoints currently read:

```csharp
        endpoints.MapGet("/api/tenants/{tenantId}/invitations", async (string tenantId, HttpRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.InvitationsViewPermission);
```

and

```csharp
        endpoints.MapPost("/api/tenants/{tenantId}/invitations", async (string tenantId, CreateInvitationRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.InvitationsCreatePermission);
```

Change both the same mechanical way:

```csharp
        endpoints.MapGet("/api/tenants/{tenantId}/invitations", async (string tenantId, HttpRequest request, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesFeature.InvitationsViewPermission);
```

```csharp
        endpoints.MapPost("/api/tenants/{tenantId}/invitations", async (string tenantId, CreateInvitationRequest request, ClaimsPrincipal principal, IamDbContext db, IAggregatedPermissionCatalog catalog) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, catalog, RolesFeature.InvitationsCreatePermission);
```

Nothing else in this file changes. Confirm there is no third call site
anywhere else by re-running
`grep -rn "AuthorizeTenantAccessAsync" src/modules/iam/` after these edits
and checking every result is one of the ones this Spec named.

## 5. `IAMConfig.cs`: register the contributor

Edit `src/modules/iam/TenantForge.Modules.Iam/IAMConfig.cs`. Add two
`using` lines at the top:

```csharp
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Iam.Features.Roles;
```

`RegisterServices` currently ends with:

```csharp
        services.AddScoped<ICredentialChecker, AccountCredentialChecker>();
        services.AddScoped<PlatformAdminSeeder>();
        // JwtIssuer only depends on the singleton AuthOptions, so it stays a
        // singleton.
        services.AddSingleton<JwtIssuer>();
    }
```

Add one line before the closing brace:

```csharp
        services.AddScoped<ICredentialChecker, AccountCredentialChecker>();
        services.AddScoped<PlatformAdminSeeder>();
        // JwtIssuer only depends on the singleton AuthOptions, so it stays a
        // singleton.
        services.AddSingleton<JwtIssuer>();

        // B034: IAM's own contribution to the shared permission catalog.
        services.AddSingleton<IPermissionCatalogContributor, IamPermissionCatalogContributor>();
    }
```

## 6. `Program.cs`: register the aggregator

Edit `src/api/TenantForge.Api/Program.cs`. It currently reads:

```csharp
using TenantForge.Api;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Shop;

var builder = WebApplication.CreateBuilder(args);
```

Add one `using`:

```csharp
using TenantForge.Api;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Shop;

var builder = WebApplication.CreateBuilder(args);
```

Further down, it currently reads:

```csharp
builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

var app = builder.Build();
```

Change it to:

```csharp
builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

// B034: one aggregate built from every registered IPermissionCatalogContributor
// (today: IAM's own; B035 adds Shop's). Registered after both modules'
// RegisterServices calls so every contributor is already in the container.
builder.Services.AddSingleton<IAggregatedPermissionCatalog>(sp =>
    new AggregatedPermissionCatalog(sp.GetServices<IPermissionCatalogContributor>()));

var app = builder.Build();
```

## 7. Update the architecture test

Edit
`tests/integration/TenantForge.Api.IntegrationTests/BuildingBlocksArchitectureTests.cs`.
`BuildingBlocks_ExportsOnlyTheApprovedProductionTypes` currently asserts:

```csharp
        Assert.Equal([
            "TenantForge.BuildingBlocks.Identifiers.TsidId",
            "TenantForge.BuildingBlocks.Modules.IModuleConfig"
        ], exportedTypes);
```

Change it to the 7 approved types, in this exact ordinal-sorted order
(verify this order yourself by running the test once — `Assert.Equal`
on an array is order-sensitive):

```csharp
        Assert.Equal([
            "TenantForge.BuildingBlocks.Identifiers.TsidId",
            "TenantForge.BuildingBlocks.Modules.IModuleConfig",
            "TenantForge.BuildingBlocks.Permissions.AggregatedPermissionCatalog",
            "TenantForge.BuildingBlocks.Permissions.IAggregatedPermissionCatalog",
            "TenantForge.BuildingBlocks.Permissions.IPermissionCatalogContributor",
            "TenantForge.BuildingBlocks.Permissions.PermissionDescriptor",
            "TenantForge.BuildingBlocks.Permissions.PermissionGroup"
        ], exportedTypes);
```

Add a `using TenantForge.BuildingBlocks.Permissions;` at the top of this
test file if any new test in this class references these types directly
(not required for this specific assertion, since it only compares
strings).

## 8. Update `docs/building-blocks/README.md`

Make every one of these edits — this is a real admission, not a
drive-by mention:

1. **Section 2 (fast facts):** change
   `| Exported public production type count | 2 — verified by ... |`
   to `| Exported public production type count | 7 — verified by ... |`
   (same test name).
2. **Section 4 (exported-type catalog):** add 5 new rows, one per new
   type, following the exact same column shape as the existing two rows
   (Type / Namespace-path / Purpose / Current consumers / Dependencies /
   Contract tests / Change risk). For `IPermissionCatalogContributor`
   and `IAggregatedPermissionCatalog`/`AggregatedPermissionCatalog`,
   "Current consumers" is `IamPermissionCatalogContributor`
   (`src/modules/iam/.../features/roles/IamPermissionCatalogContributor.cs`)
   as the sole contributor today, consumed through
   `RolesFeature.MapRolesFeature`'s injected `IAggregatedPermissionCatalog`
   — and name B035's `ShopPermissionCatalogContributor` explicitly as
   the next, already-planned second contributor (do not word this as
   hypothetical). "Dependencies" is "none (plain records/interfaces)"
   for all 5. "Contract tests" is
   `BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes`
   for all 5, plus name any new test this task or B035 adds if you add
   one specifically for the aggregator's flattening behavior (optional;
   not required by this task's own Acceptance below).
3. **Section 7 (admission checklist):** immediately below the existing
   table, add one completed evidence block (not a second empty
   template) recording the real answers: Problem = "IAM's own hardcoded
   catalog structurally blocks a second module from ever registering a
   permission key, and there is no shared shape to fix that from a
   business module without a false module→module reference." Consumers
   = "IAM (this task, migrated with zero behavior change) and Shop
   (B035, immediately next)." Ownership = "no business module should own
   another module's permission keys." Minimal API = "4 plain
   records/interfaces + 1 trivial aggregator, nothing else." Dependencies
   = "none new." Compatibility = "additive; IAM's own wire response
   shape (`PermissionCatalogResponse`) is unchanged, only its source is."
   Security = "a module's own known-keys set still gates its own
   endpoints; the aggregate only unions labels/keys for the catalog and
   for `AllKnownKeys` membership checks, never bypasses any module's own
   authorization." Tests = "`BuildingBlocksArchitectureTests.BuildingBlocks_ExportsOnlyTheApprovedProductionTypes`
   plus the full existing IAM integration suite, unmodified and passing."
   Alternatives = "keeping it IAM-local was tried; it is what caused the
   structural block this task fixes."
4. **Section 8 (explicit exclusions):** the existing row currently reads:
   `| Permission catalog and tenant authorization (RolesFeature, AuthorizationPolicyNames) | src/modules/iam/.../features/roles/, src/modules/iam/.../AuthorizationPolicyNames.cs | IAM business/domain rules, not infrastructure-neutral |`
   Change its **first cell only** to narrow what remains excluded (the
   shared catalog *shape* is now admitted; IAM's own endpoints,
   authorization decisions and policy names are not):
   `| Tenant authorization and endpoint logic (RolesFeature's handlers, AuthorizationPolicyNames) — the permission-group/descriptor *shape* itself is now shared, see Section 4 |`
   Leave the owner/reason cells as they are (still IAM-owned, still
   business/domain rules) — only the parenthetical scope narrows.
5. **Section 9 (change and compatibility policy):** no row needs new
   text; this change is the "Additive public API" row's example case
   in miniature. No edit required here beyond what Section 12 asks for.
6. **Section 12 (change-impact checklist):** state the declaration this
   task's own commit/PR body must carry:
   `BuildingBlocks docs impact: updated — Sections 2, 4, 7, 8 (5 new
   Permissions types; narrowed the RolesFeature/AuthorizationPolicyNames
   exclusion; admission evidence recorded).`

# Non-goals

- No Shop permission key, no Shop endpoint authorization change (B035).
- No change to `RolesPage.tsx` or any other frontend file (F042/F043 are
  separate, independent tasks; the catalog's *content* is unchanged by
  this task, so nothing frontend-visible changes yet).
- No change to `AuthorizationPolicyNames` (the platform-admin policy) —
  untouched, unrelated seam.

# Acceptance

- `GET /api/permissions/catalog` returns byte-identical group content
  (same ids, labels, descriptions, keys, kinds, same order) to before
  this refactor.
- Every existing role create/update/list, invitation, audit and
  `/me/permissions` integration test passes unmodified — no test file
  content changes for this task, only the architecture test's expected
  array (Scope §7).
- A role request containing an unknown permission key is still rejected
  with the same "Select only known permission keys." validation message.
- `dotnet build TenantForge.sln --nologo` succeeds with the DI container
  fully resolvable (an unregistered `IAggregatedPermissionCatalog` would
  fail every affected endpoint at first request, not at build time — the
  manual check below catches this).

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

The full existing suite must pass with zero test-content edits beyond
the one architecture-test array named in Scope §7.

Manual:

```bash
curl http://localhost:5080/api/permissions/catalog -H "Authorization: Bearer <accessToken>"
```

Expected: the exact same 3-group body (`roles`/`invitations`/`audit`)
as before this task, field-for-field.

# Lifecycle

Add row `B034` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `—`, and Spec link
`tasks/backend/B034-shared-permission-catalog-contract.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/031-cross-module-permissions-and-nav.md` is the
permanent record and is never deleted.
