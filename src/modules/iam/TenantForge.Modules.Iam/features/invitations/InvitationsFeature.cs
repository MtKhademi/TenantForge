using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Invitations;

internal static class InvitationsFeature
{
    private const int FirstPageSize = 50;
    private static readonly HashSet<string> InvitationRoles = ["Owner", "Viewer"];

    public static IEndpointRouteBuilder MapInvitationsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/invitations", async (string tenantId, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await AuthorizeTenantPermissionAsync(tenantId, principal, db, "IAM.Invitations.View");
            if (auth.Result is not null) return auth.Result;

            var now = DateTimeOffset.UtcNow;
            var invitationRows = await db.TenantInvitations.AsNoTracking()
                .Where(invitation => invitation.TenantId == auth.TenantId && invitation.Status == "Pending" && invitation.ExpiresAtUtc > now)
                .OrderByDescending(invitation => invitation.CreatedAtUtc)
                .ThenBy(invitation => invitation.Id)
                .Take(FirstPageSize)
                .ToListAsync();
            var invitations = invitationRows
                .Select(invitation => new InvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAtUtc.UtcDateTime.ToString("O"), invitation.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new InvitationListResponse(invitations));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/invitations", async (string tenantId, CreateInvitationRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await AuthorizeTenantPermissionAsync(tenantId, principal, db, "IAM.Invitations.Create");
            if (auth.Result is not null) return auth.Result;

            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var normalizedEmail = TenantInvitation.NormalizeEmail(request.Email!);
            var role = request.Role!.Trim();
            var roleExists = InvitationRoles.Contains(role) || await db.TenantRoles.AnyAsync(r => r.TenantId == auth.TenantId && r.Name == role);
            if (!roleExists)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Select an existing invitation role."] });
            }

            var now = DateTimeOffset.UtcNow;
            var duplicate = await db.TenantInvitations.AnyAsync(invitation =>
                invitation.TenantId == auth.TenantId
                && invitation.NormalizedEmail == normalizedEmail
                && invitation.Status == "Pending"
                && invitation.ExpiresAtUtc > now);
            if (duplicate) return Results.Conflict();

            var rawToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var invitation = TenantInvitation.Create(auth.TenantId, normalizedEmail, role, rawToken, now);
            db.TenantInvitations.Add(invitation);
            db.AuditEvents.Add(AuditEvent.Create(auth.TenantId, auth.AccountId, auth.Actor, auth.ActorEmail, "Invitation.Created", normalizedEmail, $"{normalizedEmail} با نقش {role} دعوت شد.", now));
            await db.SaveChangesAsync();

            var response = new InvitationResponse(invitation.Id, invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAtUtc.UtcDateTime.ToString("O"), invitation.CreatedAtUtc.UtcDateTime.ToString("O"));
            return Results.Created($"/api/tenants/{auth.TenantId}/invitations/{invitation.Id}", response);
        }).RequireAuthorization();

        return endpoints;
    }

    internal static async Task<TenantAuthorization> AuthorizeTenantPermissionAsync(string tenantId, ClaimsPrincipal principal, IamDbContext db, string permissionKey)
    {
        var tenantGuid = ParseGuid(tenantId);
        var accountId = ParseGuid(principal.FindFirstValue("sub"));
        if (tenantGuid is null || accountId is null) return TenantAuthorization.Forbidden;

        var actor = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == accountId.Value);
        if (actor is null) return TenantAuthorization.Forbidden;

        var membership = await db.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(member => member.TenantId == tenantGuid.Value && member.AccountId == accountId.Value);
        if (membership is null) return TenantAuthorization.Forbidden;

        if (membership.Role == TenantMembershipRole.Owner)
        {
            return new(tenantGuid.Value, accountId.Value, actor.DisplayName, actor.Email, null);
        }

        var hasPermission = await db.TenantMemberRoleAssignments.AsNoTracking()
            .Where(assignment => assignment.TenantMembershipId == membership.Id)
            .Join(db.TenantRoles.AsNoTracking(), assignment => assignment.TenantRoleId, role => role.Id, (_, role) => role.PermissionKeys)
            .AnyAsync(keys => keys.Contains(permissionKey));

        return hasPermission
            ? new(tenantGuid.Value, accountId.Value, actor.DisplayName, actor.Email, null)
            : TenantAuthorization.Forbidden;
    }

    private static Dictionary<string, string[]> Validate(CreateInvitationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            errors["role"] = ["Select a role."];
        }

        return errors;
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty ? guid : null;
}

internal sealed record TenantAuthorization(Guid TenantId, Guid AccountId, string Actor, string ActorEmail, IResult? Result)
{
    public static TenantAuthorization Forbidden { get; } = new(Guid.Empty, Guid.Empty, string.Empty, string.Empty, Results.Forbid());
}

internal sealed record CreateInvitationRequest(string? Email, string? Role);
internal sealed record InvitationListResponse(IReadOnlyList<InvitationResponse> Invitations);
internal sealed record InvitationResponse(Guid Id, string Email, string Role, string Status, string ExpiresAtUtc, string CreatedAtUtc);
