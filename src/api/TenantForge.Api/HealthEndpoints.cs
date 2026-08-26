using Microsoft.AspNetCore.Http;

namespace TenantForge.Api;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (HttpContext context) =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Results.Text("ok");
        });
        return endpoints;
    }
}
