using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace TenantForge.Modules.Iam.Features.Login;

internal static class LoginFeature
{
    public static IEndpointRouteBuilder MapLoginFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", (LoginRequest request, [FromServices] ICredentialChecker? checker, [FromServices] DevelopmentJwtIssuer? issuer, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("TenantForge.Modules.Iam.Features.Login");
            var email = request.Email?.Trim() ?? string.Empty;
            var password = request.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Results.Problem(
                    title: "Invalid request",
                    detail: "Email and password are required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (checker is null || issuer is null)
            {
                logger.LogInformation("Login rejected: no credential checker is registered in this environment.");
                return Results.Problem(
                    title: "Invalid credentials",
                    detail: "The email or password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!checker.Check(email, password))
            {
                logger.LogInformation("Login failed for email {Email}.", email);
                return Results.Problem(
                    title: "Invalid credentials",
                    detail: "The email or password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            logger.LogInformation("Login succeeded for email {Email}.", email);
            var (accessToken, expiresAtUtc) = issuer.IssueForAdmin(email);

            var response = new LoginResponse(
                accessToken,
                expiresAtUtc.UtcDateTime.ToString("O"),
                new LoginUserResponse(
                    DevelopmentAdminIdentity.Id,
                    email,
                    DevelopmentAdminIdentity.DisplayName,
                    DevelopmentAdminIdentity.IsPlatformAdmin));

            return Results.Ok(response);
        });

        return endpoints;
    }
}
