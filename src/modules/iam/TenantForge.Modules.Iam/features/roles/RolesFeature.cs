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
    private const string OwnerPermission = "IAM.Tenants.Create";

    private static readonly PermissionGroupResponse[] CatalogGroups =
    [
        new("dashboard", "داشبورد", "دسترسی خواندن به نمای خلاصه وضعیت مستأجر.",
        [
            new("IAM.Dashboard.View", "مشاهده داشبورد", "اجازه دیدن خلاصه‌ها و شاخص‌های صفحه داشبورد.", "read")
        ]),
        new("users", "کاربران", "مدیریت کاربران", 
        [
            new("IAM.Users.View", "مشاهده کاربران", "اجازه دیدن فهرست کاربران.", "read"),
            new("IAM.Users.Create", "ایجاد کاربر", "اجازه ایجاد کاربر جدید.", "write")
        ]),
        new("tenants", "مستأجرها", "مدیریت مستأجرها", 
        [
            new("IAM.Tenants.View", "مشاهده مستأجرها", "اجازه دیدن داده‌های مستأجر.", "read"),
            new("IAM.Tenants.Create", "ایجاد مستأجر", "اجازه ایجاد مستأجر و مالک نخست.", "write")
        ])
    ];

    private static readonly HashSet<string> PermissionKeys = CatalogGroups
        .SelectMany(group => group.Permissions)
        .Select(permission => permission.Key)
        .ToHashSet(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapRolesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/permissions/catalog", () => Results.Ok(new PermissionCatalogResponse(CatalogGroups)))
            .RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/roles", async (string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantGuid = ParseTenantId(tenantId);
            var accountId = GetAuthenticatedAccountId(principal);
            if (tenantGuid is null || accountId is null || !await IsActiveMemberAsync(db, tenantGuid.Value, accountId.Value))
            {
                return Results.Forbid();
            }

            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, tenantGuid.Value)));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/roles", async (string tenantId, CreateRoleRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantGuid = ParseTenantId(tenantId);
            var accountId = GetAuthenticatedAccountId(principal);
            if (tenantGuid is null || accountId is null || !await IsOwnerAsync(db, tenantGuid.Value, accountId.Value))
            {
                return Results.Forbid();
            }

            var errors = ValidateRoleRequest(request.Name, request.PermissionKeys, requireName: true);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var role = TenantRole.Create(tenantGuid.Value, request.Name!, request.PermissionKeys ?? [], DateTimeOffset.UtcNow);
            db.TenantRoles.Add(role);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                db.ChangeTracker.Clear();
                return DuplicateRoleProblem();
            }

            var response = (await BuildRoleResponsesAsync(db, tenantGuid.Value)).Single(item => item.Id == role.Id);
            return Results.Created($"/api/tenants/{tenantGuid}/roles/{role.Id}", response);
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/roles/{roleId}", async (string tenantId, string roleId, UpdateRoleRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantGuid = ParseTenantId(tenantId);
            var roleGuid = ParseTenantId(roleId);
            var accountId = GetAuthenticatedAccountId(principal);
            if (tenantGuid is null || roleGuid is null || accountId is null || !await IsOwnerAsync(db, tenantGuid.Value, accountId.Value))
            {
                return Results.Forbid();
            }

            var errors = ValidateRoleRequest(null, request.PermissionKeys, requireName: false);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var role = await db.TenantRoles.SingleOrDefaultAsync(item => item.TenantId == tenantGuid.Value && item.Id == roleGuid.Value);
            if (role is null)
            {
                return Results.NotFound();
            }

            if (role.Kind != "custom")
            {
                return Results.Conflict();
            }

            if (IsOwnerRole(role) && !request.PermissionKeys!.Contains(OwnerPermission, StringComparer.Ordinal) && await OwnerCountAsync(db, tenantGuid.Value) <= 1)
            {
                return Results.Conflict();
            }

            role.ReplacePermissions(request.PermissionKeys!, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok((await BuildRoleResponsesAsync(db, tenantGuid.Value)).Single(item => item.Id == role.Id));
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db);
            if (parsed.Result is not null) return parsed.Result;

            var exists = await db.TenantMemberRoleAssignments.AnyAsync(a => a.TenantMembershipId == parsed.MemberId && a.TenantRoleId == parsed.RoleId);
            if (!exists)
            {
                db.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(parsed.MemberId, parsed.RoleId, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
        }).RequireAuthorization();

        endpoints.MapDelete("/api/tenants/{tenantId}/members/{memberId}/roles/{roleId}", async (string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var parsed = await ValidateAssignmentAsync(tenantId, memberId, roleId, principal, db);
            if (parsed.Result is not null) return parsed.Result;

            var assignment = await db.TenantMemberRoleAssignments.SingleOrDefaultAsync(a => a.TenantMembershipId == parsed.MemberId && a.TenantRoleId == parsed.RoleId);
            if (assignment is null)
            {
                return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
            }

            var role = await db.TenantRoles.SingleAsync(r => r.Id == parsed.RoleId);
            if (IsOwnerRole(role) && await OwnerCountAsync(db, parsed.TenantId) <= 1)
            {
                return Results.Conflict();
            }

            db.TenantMemberRoleAssignments.Remove(assignment);
            await db.SaveChangesAsync();
            return Results.Ok(new TenantRolesResponse(await BuildRoleResponsesAsync(db, parsed.TenantId)));
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/me/permissions", async (string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var tenantGuid = ParseTenantId(tenantId);
            var accountId = GetAuthenticatedAccountId(principal);
            if (tenantGuid is null || accountId is null || !await IsActiveMemberAsync(db, tenantGuid.Value, accountId.Value))
            {
                return Results.Forbid();
            }

            var permissions = await ResolvePermissionsAsync(db, tenantGuid.Value, accountId.Value);
            return Results.Ok(new ResolvedPermissionsResponse(permissions));
        }).RequireAuthorization();

        return endpoints;
    }

    internal static async Task<bool> HasPermissionAsync(IamDbContext db, Guid tenantId, Guid accountId, string permissionKey) =>
        (await ResolvePermissionsAsync(db, tenantId, accountId)).Contains(permissionKey, StringComparer.Ordinal);

    private static async Task<AssignmentValidation> ValidateAssignmentAsync(string tenantId, string memberId, string roleId, ClaimsPrincipal principal, IamDbContext db)
    {
        var tenantGuid = ParseTenantId(tenantId);
        var memberGuid = ParseTenantId(memberId);
        var roleGuid = ParseTenantId(roleId);
        var accountId = GetAuthenticatedAccountId(principal);
        if (tenantGuid is null || memberGuid is null || roleGuid is null || accountId is null || !await IsOwnerAsync(db, tenantGuid.Value, accountId.Value))
        {
            return new(Guid.Empty, Guid.Empty, Guid.Empty, Results.Forbid());
        }

        var memberExists = await db.TenantMemberships.AnyAsync(member => member.TenantId == tenantGuid.Value && member.Id == memberGuid.Value);
        var roleExists = await db.TenantRoles.AnyAsync(role => role.TenantId == tenantGuid.Value && role.Id == roleGuid.Value);
        if (!memberExists || !roleExists)
        {
            return new(tenantGuid.Value, memberGuid.Value, roleGuid.Value, Results.NotFound());
        }

        return new(tenantGuid.Value, memberGuid.Value, roleGuid.Value, null);
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
        var membership = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(member => member.TenantId == tenantId && member.AccountId == accountId);
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
        foreach (var set in assignedKeys) keys.UnionWith(set);
        return keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    private static async Task<bool> IsActiveMemberAsync(IamDbContext db, Guid tenantId, Guid accountId) =>
        await db.TenantMemberships.AnyAsync(member => member.TenantId == tenantId && member.AccountId == accountId);

    private static async Task<bool> IsOwnerAsync(IamDbContext db, Guid tenantId, Guid accountId) =>
        await HasPermissionAsync(db, tenantId, accountId, OwnerPermission);

    private static async Task<int> OwnerCountAsync(IamDbContext db, Guid tenantId)
    {
        var builtInOwners = await db.TenantMemberships.CountAsync(member => member.TenantId == tenantId && member.Role == TenantMembershipRole.Owner);
        var customOwners = await db.TenantMemberRoleAssignments
            .Join(db.TenantMemberships.Where(member => member.TenantId == tenantId), assignment => assignment.TenantMembershipId, member => member.Id, (assignment, member) => assignment)
            .Join(db.TenantRoles.Where(role => role.TenantId == tenantId && role.PermissionKeys.Contains(OwnerPermission)), assignment => assignment.TenantRoleId, role => role.Id, (_, _) => 1)
            .CountAsync();
        return builtInOwners + customOwners;
    }

    private static bool IsOwnerRole(TenantRole role) => role.PermissionKeys.Contains(OwnerPermission, StringComparer.Ordinal);

    private static Dictionary<string, string[]> ValidateRoleRequest(string? name, IReadOnlyList<string>? permissionKeys, bool requireName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (requireName)
        {
            var trimmedName = name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedName)) errors["name"] = ["Role name is required."];
            else if (trimmedName.Length > 80) errors["name"] = ["Role name must be 80 characters or fewer."];
        }

        if (permissionKeys is null || permissionKeys.Count == 0) errors["permissionKeys"] = ["Select at least one permission."];
        else if (permissionKeys.Any(key => !PermissionKeys.Contains(key))) errors["permissionKeys"] = ["Select only known permission keys."];
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

internal sealed record PermissionCatalogResponse(IReadOnlyList<PermissionGroupResponse> Groups);
internal sealed record PermissionGroupResponse(string Id, string Label, string Description, IReadOnlyList<PermissionResponse> Permissions);
internal sealed record PermissionResponse(string Key, string Label, string Description, string Kind);
internal sealed record TenantRolesResponse(IReadOnlyList<TenantRoleResponse> Roles);
internal sealed record TenantRoleResponse(Guid Id, string Name, string Description, string Kind, IReadOnlyList<string> PermissionKeys, IReadOnlyList<Guid> MemberIds, string CreatedAtUtc, string UpdatedAtUtc);
internal sealed record CreateRoleRequest(string? Name, IReadOnlyList<string>? PermissionKeys);
internal sealed record UpdateRoleRequest(IReadOnlyList<string>? PermissionKeys);
internal sealed record ResolvedPermissionsResponse(IReadOnlyList<string> Permissions);
internal sealed record AssignmentValidation(Guid TenantId, Guid MemberId, Guid RoleId, IResult? Result);
