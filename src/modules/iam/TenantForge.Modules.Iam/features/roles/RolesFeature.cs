using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
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

    private static readonly PermissionGroupResponse[] CatalogGroups =
    [
        new("roles", "نقش‌ها", "مدیریت نقش‌ها و مجوزهای مستأجر.",
        [
            new(RolesManagePermission, "مدیریت نقش‌ها", "اجازه ایجاد، ویرایش و تخصیص نقش‌های مستأجر.", "write")
        ]),
        new("invitations", "دعوت‌ها", "مدیریت دعوت‌نامه‌های مستأجر.",
        [
            new(InvitationsViewPermission, "مشاهده دعوت‌ها", "اجازه دیدن دعوت‌نامه‌های در انتظار.", "read"),
            new(InvitationsCreatePermission, "ایجاد دعوت", "اجازه ایجاد دعوت‌نامه جدید برای مستأجر.", "write")
        ]),
        new("audit", "گزارش فعالیت", "دسترسی به رویدادهای ثبت‌شده مستأجر.",
        [
            new(AuditViewPermission, "مشاهده گزارش فعالیت", "اجازه خواندن گزارش فعالیت مستأجر.", "read")
        ])
    ];

    private static readonly HashSet<string> PermissionKeys = CatalogGroups
        .SelectMany(group => group.Permissions)
        .Select(permission => permission.Key)
        .ToHashSet(StringComparer.Ordinal);

    internal static IReadOnlySet<string> KnownPermissionKeys => PermissionKeys;

    public static IEndpointRouteBuilder MapRolesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/permissions/catalog", () => Results.Ok(new PermissionCatalogResponse(CatalogGroups)))
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

        endpoints.MapPost("/api/tenants/{tenantId}/roles", async (string tenantId, CreateRoleRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission);
            if (access.Result is not null) return access.Result;

            var errors = ValidateRoleRequest(request.Name, request.PermissionKeys, requireName: true);
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

        endpoints.MapPut("/api/tenants/{tenantId}/roles/{roleId}", async (string tenantId, string roleId, UpdateRoleRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantTsid = ParseTenantId(tenantId);
            var roleTsid = ParseTenantId(roleId);
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission);
            if (tenantTsid is null || roleTsid is null || access.Result is not null)
            {
                return access.Result ?? Results.Forbid();
            }

            var errors = ValidateRoleRequest(null, request.PermissionKeys, requireName: false);
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

        endpoints.MapPut("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db);
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

        endpoints.MapDelete("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db);
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

        endpoints.MapGet("/api/tenants/{tenantId}/me/permissions", async (string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var permissions = await ResolvePermissionsAsync(db, access.TenantId, access.AccountId);
            return Results.Ok(new ResolvedPermissionsResponse(permissions));
        }).RequireAuthorization();

        return endpoints;
    }

    internal static async Task<TenantAccess> AuthorizeTenantAccessAsync(string tenantId, ClaimsPrincipal principal, IamDbContext db, string? permissionKey = null)
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
            var permissions = await ResolvePermissionsAsync(db, tenantTsid.Value, accountId.Value);
            if (!permissions.Contains(permissionKey, StringComparer.Ordinal))
            {
                return TenantAccess.Forbidden;
            }
        }

        return new(tenantTsid.Value, accountId.Value, row.Membership.Id, row.Membership.Role, row.Account.DisplayName, row.Account.Email, null);
    }

    internal static async Task<bool> HasPermissionAsync(IamDbContext db, Tsid tenantId, Tsid accountId, string permissionKey) =>
        (await ResolvePermissionsAsync(db, tenantId, accountId)).Contains(permissionKey, StringComparer.Ordinal);

    private static async Task<AssignmentValidation> ValidateAssignmentAsync(string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db)
    {
        var memberTsid = ParseTenantId(memberId);
        var roleTsid = ParseTenantId(roleId);
        var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission);
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

    private static async Task<IReadOnlyList<string>> ResolvePermissionsAsync(IamDbContext db, Tsid tenantId, Tsid accountId)
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
            keys.UnionWith(PermissionKeys);
        }

        var assignedKeys = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantMembershipId == membership.Id)
            .Join(db.TenantRoles.AsNoTracking(), assignment => assignment.TenantRoleId, role => role.Id, (_, role) => role.PermissionKeys)
            .ToListAsync();
        foreach (var set in assignedKeys)
        {
            keys.UnionWith(set.Where(PermissionKeys.Contains));
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

    private static Dictionary<string, string[]> ValidateRoleRequest(string? name, IReadOnlyList<string>? permissionKeys, bool requireName)
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
        else if (permissionKeys.Any(key => !PermissionKeys.Contains(key)))
        {
            errors["permissionKeys"] = ["Select only known permission keys."];
        }

        return errors;
    }

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
