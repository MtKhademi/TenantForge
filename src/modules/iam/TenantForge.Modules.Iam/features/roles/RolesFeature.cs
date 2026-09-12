using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;

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

        endpoints.MapGet("/api/tenants/{tenantId}/roles", async (string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, access.TenantId)));
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

            var response = (await BuildRoleResponsesAsync(db, access.TenantId)).Single(item => item.Id == role.Id);
            return Results.Created($"/api/tenants/{access.TenantId}/roles/{role.Id}", response);
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/roles/{roleId}", async (string tenantId, string roleId, UpdateRoleRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantGuid = ParseTenantId(tenantId);
            var roleGuid = ParseTenantId(roleId);
            var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission);
            if (tenantGuid is null || roleGuid is null || access.Result is not null)
            {
                return access.Result ?? Results.Forbid();
            }

            var errors = ValidateRoleRequest(null, request.PermissionKeys, requireName: false);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            await using var transaction = await BeginTenantMutationAsync(db, access.TenantId);
            var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.TenantId == access.TenantId && item.Id == roleGuid.Value);
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
            return Results.Ok((await BuildRoleResponsesAsync(db, access.TenantId)).Single(item => item.Id == role.Id));
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
        var tenantGuid = ParseTenantId(tenantId);
        var accountId = GetAuthenticatedAccountId(principal);
        if (tenantGuid is null || accountId is null)
        {
            return TenantAccess.Forbidden;
        }

        var row = await db.TenantMemberships.AsNoTracking()
            .Where(member => member.TenantId == tenantGuid.Value && member.AccountId == accountId.Value)
            .Join(db.Accounts.AsNoTracking().Where(account => account.Status == AccountStatus.Active), member => member.AccountId, account => account.Id, (member, account) => new { Membership = member, Account = account })
            .Join(db.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active), row => row.Membership.TenantId, tenant => tenant.Id, (row, tenant) => row)
            .SingleOrDefaultAsync();

        if (row is null)
        {
            return TenantAccess.Forbidden;
        }

        if (permissionKey is not null)
        {
            var permissions = await ResolvePermissionsAsync(db, tenantGuid.Value, accountId.Value);
            if (!permissions.Contains(permissionKey, StringComparer.Ordinal))
            {
                return TenantAccess.Forbidden;
            }
        }

        return new(tenantGuid.Value, accountId.Value, row.Membership.Id, row.Membership.Role, row.Account.DisplayName, row.Account.Email, null);
    }

    internal static async Task<bool> HasPermissionAsync(IamDbContext db, Guid tenantId, Guid accountId, string permissionKey) =>
        (await ResolvePermissionsAsync(db, tenantId, accountId)).Contains(permissionKey, StringComparer.Ordinal);

    private static async Task<AssignmentValidation> ValidateAssignmentAsync(string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db)
    {
        var memberGuid = ParseTenantId(memberId);
        var roleGuid = ParseTenantId(roleId);
        var access = await AuthorizeTenantAccessAsync(tenantId, principal, db, RolesManagePermission);
        if (memberGuid is null || roleGuid is null || access.Result is not null)
        {
            return AssignmentValidation.Forbidden;
        }

        var memberExists = await db.TenantMemberships.AnyAsync(member => member.TenantId == access.TenantId && member.Id == memberGuid.Value);
        var roleExists = await db.TenantRoles.AnyAsync(role => role.TenantId == access.TenantId && role.Id == roleGuid.Value);
        if (!memberExists || !roleExists)
        {
            return new(access.TenantId, memberGuid.Value, roleGuid.Value, access.AccountId, access.Actor, access.ActorEmail, Results.NotFound());
        }

        return new(access.TenantId, memberGuid.Value, roleGuid.Value, access.AccountId, access.Actor, access.ActorEmail, null);
    }

    private static async Task<IReadOnlyList<TenantRoleResponse>> BuildRoleResponsesAsync(IamDbContext db, Guid tenantId)
    {
        var roles = await db.TenantRoles.AsNoTracking().Where(role => role.TenantId == tenantId).OrderBy(role => role.Name).ToListAsync();
        var assignments = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Join(db.TenantMemberships.AsNoTracking().Where(member => member.TenantId == tenantId), assignment => assignment.TenantMembershipId, member => member.Id, (assignment, member) => new { assignment.TenantRoleId, MemberId = member.Id })
            .ToListAsync();

        return roles.Select(role => new TenantRoleResponse(
            role.Id,
            role.Name,
            role.Description,
            role.Kind,
            role.PermissionKeys,
            assignments.Where(a => a.TenantRoleId == role.Id).Select(a => a.MemberId).OrderBy(id => id).ToList(),
            role.CreatedAtUtc.UtcDateTime.ToString("O"),
            role.UpdatedAtUtc.UtcDateTime.ToString("O"))).ToList();
    }

    private static async Task<IReadOnlyList<string>> ResolvePermissionsAsync(IamDbContext db, Guid tenantId, Guid accountId)
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

    private static async Task<bool> HasAnyEffectiveRoleAdministratorAsync(IamDbContext db, Guid tenantId, Guid? updatedRoleId, IReadOnlyList<string>? updatedPermissionKeys, RemovedAssignment? removedAssignment)
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

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTenantMutationAsync(IamDbContext db, Guid tenantId)
    {
        var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString()}, 0))");
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

    private static Guid? ParseTenantId(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    private static Guid? GetAuthenticatedAccountId(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true }) return null;
        var subject = principal.FindFirstValue("sub");
        return Guid.TryParse(subject, out var accountId) && accountId != Guid.Empty ? accountId : null;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult DuplicateRoleProblem() => Results.Problem(title: "Duplicate tenant role", detail: "A role with this name already exists in this tenant.", statusCode: StatusCodes.Status409Conflict);
}

internal sealed record TenantAccess(Guid TenantId, Guid AccountId, Guid MembershipId, TenantMembershipRole MembershipRole, string Actor, string ActorEmail, IResult? Result)
{
    public bool IsOwner => MembershipRole == TenantMembershipRole.Owner;
    public static TenantAccess Forbidden { get; } = new(Guid.Empty, Guid.Empty, Guid.Empty, TenantMembershipRole.Member, string.Empty, string.Empty, Results.Forbid());
}

internal sealed record ActorSnapshot(string DisplayName, string Email);
internal sealed record PermissionCatalogResponse(IReadOnlyList<PermissionGroupResponse> Groups);
internal sealed record PermissionGroupResponse(string Id, string Label, string Description, IReadOnlyList<PermissionResponse> Permissions);
internal sealed record PermissionResponse(string Key, string Label, string Description, string Kind);
internal sealed record TenantRolesResponse(IReadOnlyList<TenantRoleResponse> Roles);
internal sealed record TenantRoleResponse(Guid Id, string Name, string Description, string Kind, IReadOnlyList<string> PermissionKeys, IReadOnlyList<Guid> MemberIds, string CreatedAtUtc, string UpdatedAtUtc);
internal sealed record CreateRoleRequest(string? Name, IReadOnlyList<string>? PermissionKeys);
internal sealed record UpdateRoleRequest(IReadOnlyList<string>? PermissionKeys);
internal sealed record ResolvedPermissionsResponse(IReadOnlyList<string> Permissions);
internal sealed record AssignmentValidation(Guid TenantId, Guid MemberId, Guid RoleId, Guid AccountId, string Actor, string ActorEmail, IResult? Result)
{
    public static AssignmentValidation Forbidden { get; } = new(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty, Results.Forbid());
}
internal sealed record RemovedAssignment(Guid TenantMembershipId, Guid TenantRoleId);
