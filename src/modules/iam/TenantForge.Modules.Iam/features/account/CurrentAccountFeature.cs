using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace TenantForge.Modules.Iam.Features.Account;

internal static class CurrentAccountFeature
{
    public static IEndpointRouteBuilder MapCurrentAccountFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
        {
            // Fail closed: every identity field comes from the server-validated
            // token claims. No claim is trusted unless the authentication handler
            // produced it, and a missing claim means the caller is not authenticated.
            if (principal.Identity is not { IsAuthenticated: true })
            {
                return Results.Unauthorized();
            }

            var id = principal.FindFirstValue("sub");
            var email = principal.FindFirstValue("email");
            var displayName = principal.FindFirstValue("name");
            var isPlatformAdmin = bool.TryParse(
                principal.FindFirstValue("isPlatformAdmin"), out var admin) && admin;

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(displayName)
                || !isPlatformAdmin)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new CurrentAccountResponse(id, email, displayName, isPlatformAdmin));
        })
        .RequireAuthorization();

        return endpoints;
    }
}
