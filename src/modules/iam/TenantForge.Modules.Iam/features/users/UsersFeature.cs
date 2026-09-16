using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Contract.Requests;
using TenantForge.Modules.Iam.Contract.Responses;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Pagination;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Users;

internal static class UsersFeature
{
    private const int MinimumPasswordLength = 8;

    public static IEndpointRouteBuilder MapUsersFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/users", async (HttpRequest request, IamDbContext db) =>
        {
            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var query = db.Accounts
                .AsNoTracking()
                .OrderBy(account => account.CreatedAtUtc)
                .ThenBy(account => account.Id);

            var (accounts, pagination) = await PaginationSupport.PageAsync(query, page);
            var users = accounts.Select(FromAccount).ToList();
            return Results.Ok(new UsersListResponse(users, pagination));
        })
        // S11: the global account directory belongs exclusively to a platform
        // administrator. The named claim policy (registered in IAMConfig) does
        // the authorization: unauthenticated -> 401 (challenge), authenticated
        // but missing/incorrect isPlatformAdmin claim -> 403 (forbid). A
        // supplied tenantId is deliberately not bound at all, so it can never
        // grant platform authority (see docs/design/s11-platform-tenant-boundaries.md).
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        endpoints.MapPost("/api/platform/users", async (
            CreateUserRequest request,
            [FromServices] IamDbContext db,
            [FromServices] IPasswordHasher<global::TenantForge.Modules.Iam.Domain.Account> passwordHasher) =>
        {
            // S11: platform account creation is admin-only; tenantId never grants
            // it. See the GET mapping above for the policy's 401/403 semantics.
            var validationErrors = Validate(request);
            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            var email = request.Email!.Trim();
            var displayName = request.DisplayName!.Trim();
            var normalizedEmail = global::TenantForge.Modules.Iam.Domain.Account.NormalizeEmail(email);

            var duplicateExists = await db.Accounts.AnyAsync(account => account.NormalizedEmail == normalizedEmail);
            if (duplicateExists)
            {
                return DuplicateEmailProblem();
            }

            var passwordHash = passwordHasher.HashPassword(null!, request.Password!);
            var account = global::TenantForge.Modules.Iam.Domain.Account.CreateUser(email, displayName, passwordHash, DateTimeOffset.UtcNow);

            db.Accounts.Add(account);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return DuplicateEmailProblem();
            }

            var response = FromAccount(account);
            return Results.Created($"/api/platform/users/{TsidId.Format(account.Id)}", response);
        })
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        return endpoints;
    }

    private static Dictionary<string, string[]> Validate(CreateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Email is required."];
        }
        else if (email.Length > 320 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors["displayName"] = ["Display name is required."];
        }
        else if (displayName.Length > 200)
        {
            errors["displayName"] = ["Display name must be 200 characters or fewer."];
        }

        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            errors["password"] = ["Password is required."];
        }
        else if (password.Length < MinimumPasswordLength)
        {
            errors["password"] = [$"Password must be at least {MinimumPasswordLength} characters."];
        }

        return errors;
    }

    private static IResult DuplicateEmailProblem() => Results.Problem(
        title: "Duplicate email",
        detail: "An account with this email already exists.",
        statusCode: StatusCodes.Status409Conflict);

    // B022/S23: UserResponse moved to TenantForge.Modules.Iam.Contract as a
    // data-only record. This mapper stays module-owned because it references
    // the internal domain entity Account — a contract type may never do that.
    private static UserResponse FromAccount(global::TenantForge.Modules.Iam.Domain.Account account) => new(
        TsidId.Format(account.Id),
        account.Email,
        account.DisplayName,
        account.Status.ToString(),
        account.IsPlatformAdmin,
        account.CreatedAtUtc.UtcDateTime.ToString("O"));
}
