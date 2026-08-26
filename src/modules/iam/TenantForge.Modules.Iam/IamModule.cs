using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Iam.Features.Login;

namespace TenantForge.Modules.Iam;

public static class IamModule
{
    private static readonly IModuleConfig Config = new IAMConfig();

    public static IServiceCollection AddIamModule(this IServiceCollection services, IHostEnvironment environment)
    {
        Config.RegisterServices(services, environment);
        return services;
    }

    public static void ValidateIamModuleConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        Config.ValidateConfiguration(environment, configuration);
    }

    public static IEndpointRouteBuilder MapIamModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLoginFeature();
        return endpoints;
    }
}
