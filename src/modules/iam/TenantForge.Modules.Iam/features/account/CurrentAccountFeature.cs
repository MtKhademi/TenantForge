using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TenantForge.Modules.Iam.Domain;

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

            // B017/S19: a token whose subject is not a canonical TSID string
            // (e.g. a legacy GUID-subject token minted before the identifier
            // migration) is rejected here, at authentication time, rather than
            // forwarded as an opaque string. This is the point at which the old
            // identity representation stops being accepted: the token still
            // validates cryptographically, but its subject no longer names a
            // live account shape, so the caller must sign in again.
            if (!IamId.TryParse(id, out var accountId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(displayName))
            {
                return Results.Unauthorized();
            }

            // Re-emit in canonical form: even a lower-case subject is accepted
            // (Crockford base32 is case-insensitive) but always normalized.
            return Results.Ok(new CurrentAccountResponse(IamId.Format(accountId), email, displayName, isPlatformAdmin));
        })
        .RequireAuthorization();

        return endpoints;
    }
}
