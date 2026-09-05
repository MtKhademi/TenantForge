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
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, [FromServices] ICredentialChecker checker, [FromServices] JwtIssuer issuer, ILoggerFactory loggerFactory) =>
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

            if (!issuer.CanIssue)
            {
                // No signing key is configured: refuse to authenticate rather
                // than mint an unverifiable token. This keeps the endpoint
                // fail-closed in any environment that lacks auth configuration.
                logger.LogInformation("Login rejected: no signing key is configured in this environment.");
                return Results.Problem(
                    title: "Invalid credentials",
                    detail: "The email or password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // The checker consults the persisted account store. A null result
            // means: unknown email, wrong password, or disabled account — all
            // deliberately indistinguishable to the caller.
            var account = await checker.AuthenticateAsync(email, password);
            if (account is null)
            {
                logger.LogInformation("Login failed for email {Email}.", email);
                return Results.Problem(
                    title: "Invalid credentials",
                    detail: "The email or password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            logger.LogInformation("Login succeeded for email {Email}.", email);
            var (accessToken, expiresAtUtc) = issuer.Issue(account);

            var response = new LoginResponse(
                accessToken,
                expiresAtUtc.UtcDateTime.ToString("O"),
                new LoginUserResponse(
                    account.Id,
                    account.Email,
                    account.DisplayName,
                    account.IsPlatformAdmin));

            return Results.Ok(response);
        });

        return endpoints;
    }
}
