using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Contract.Responses;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Features.Roles;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Invitations;

internal static class InvitationsFeature
{
    private static readonly HashSet<string> InvitationRoles = ["Owner", "Viewer"];

    public static IEndpointRouteBuilder MapInvitationsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenants/{tenantId}/invitations", async (string tenantId, HttpRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.InvitationsViewPermission);
            if (auth.Result is not null) return auth.Result;
            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var now = DateTimeOffset.UtcNow;
            var query = db.TenantInvitations.AsNoTracking()
                .Where(invitation => invitation.TenantId == auth.TenantId && invitation.Status == "Pending" && invitation.ExpiresAtUtc > now)
                .OrderByDescending(invitation => invitation.CreatedAtUtc)
                .ThenBy(invitation => invitation.Id);
            var (invitationRows, pagination) = await PaginationSupport.PageAsync(query, page);
            var invitations = invitationRows
                .Select(invitation => new InvitationResponse(TsidId.Format(invitation.Id), invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAtUtc.UtcDateTime.ToString("O"), invitation.CreatedAtUtc.UtcDateTime.ToString("O")))
                .ToList();

            return Results.Ok(new InvitationListResponse(invitations, pagination));
        }).RequireAuthorization();

        endpoints.MapPost("/api/tenants/{tenantId}/invitations", async (string tenantId, CreateInvitationRequest request, ClaimsPrincipal principal, IamDbContext db) =>
        {
            var auth = await RolesFeature.AuthorizeTenantAccessAsync(tenantId, principal, db, RolesFeature.InvitationsCreatePermission);
            if (auth.Result is not null) return auth.Result;

            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var normalizedEmail = TenantInvitation.NormalizeEmail(request.Email!);
            var role = request.Role!.Trim();

            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({TsidId.Format(auth.TenantId) + ":" + normalizedEmail}, 0))");

            var roleExists = InvitationRoles.Contains(role) || await db.TenantRoles.AnyAsync(r => r.TenantId == auth.TenantId && r.Name == role);
            if (!roleExists)
            {
                await transaction.RollbackAsync();
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Select an existing invitation role."] });
            }

            var now = DateTimeOffset.UtcNow;
            var duplicate = await db.TenantInvitations.AnyAsync(invitation =>
                invitation.TenantId == auth.TenantId
                && invitation.NormalizedEmail == normalizedEmail
                && invitation.Status == "Pending"
                && invitation.ExpiresAtUtc > now);
            if (duplicate)
            {
                await transaction.RollbackAsync();
                return Results.Conflict();
            }

            var rawToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var invitation = TenantInvitation.Create(auth.TenantId, normalizedEmail, role, rawToken, now);
            db.TenantInvitations.Add(invitation);
            db.AuditEvents.Add(AuditEvent.Create(auth.TenantId, auth.AccountId, auth.Actor, auth.ActorEmail, "Invitation.Created", normalizedEmail, $"{normalizedEmail} با نقش {role} دعوت شد.", now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = new InvitationResponse(TsidId.Format(invitation.Id), invitation.Email, invitation.Role, invitation.Status, invitation.ExpiresAtUtc.UtcDateTime.ToString("O"), invitation.CreatedAtUtc.UtcDateTime.ToString("O"));
            return Results.Created($"/api/tenants/{TsidId.Format(auth.TenantId)}/invitations/{TsidId.Format(invitation.Id)}", response);
        }).RequireAuthorization();

        return endpoints;
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
}

internal sealed record CreateInvitationRequest(string? Email, string? Role);
internal sealed record InvitationListResponse(IReadOnlyList<InvitationResponse> Invitations, PaginationMetadata Pagination);
internal sealed record InvitationResponse(string Id, string Email, string Role, string Status, string ExpiresAtUtc, string CreatedAtUtc);
