using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam.Features.Users;

internal static class UsersFeature
{
    private const int FirstPageSize = 50;
    private const int MinimumPasswordLength = 8;

    public static IEndpointRouteBuilder MapUsersFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/users", async (IamDbContext db) =>
        {
            var accounts = await db.Accounts
                .AsNoTracking()
                .OrderBy(account => account.CreatedAtUtc)
                .ThenBy(account => account.Id)
                .Take(FirstPageSize)
                .ToListAsync();

            var users = accounts.Select(UserResponse.FromAccount).ToList();
            return Results.Ok(new UsersListResponse(users));
        })
        .RequireAuthorization(AuthorizationPolicyNames.PlatformAdmin);

        endpoints.MapPost("/api/platform/users", async (
            CreateUserRequest request,
            [FromServices] IamDbContext db,
            [FromServices] IPasswordHasher<global::TenantForge.Modules.Iam.Domain.Account> passwordHasher) =>
        {
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

            var response = UserResponse.FromAccount(account);
            return Results.Created($"/api/platform/users/{account.Id}", response);
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
}

internal sealed record CreateUserRequest(string? Email, string? DisplayName, string? Password);

internal sealed record UsersListResponse(IReadOnlyList<UserResponse> Users);

internal sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsPlatformAdmin,
    string CreatedAtUtc)
{
    public static UserResponse FromAccount(global::TenantForge.Modules.Iam.Domain.Account account) => new(
        account.Id,
        account.Email,
        account.DisplayName,
        account.Status.ToString(),
        account.IsPlatformAdmin,
        account.CreatedAtUtc.UtcDateTime.ToString("O"));
}
