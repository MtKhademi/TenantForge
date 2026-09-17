using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop;

public static class ShopModule
{
    private static readonly IModuleConfig Config = new ShopConfig();

    /// <summary>
    /// Registration phase: mirrors IamModule.AddIamModule exactly — adds
    /// every Shop-owned service to the container, does no I/O and no
    /// pass/fail decision. Runs before builder.Build().
    /// </summary>
    public static IServiceCollection AddShopModule(this IServiceCollection services, IHostEnvironment environment)
    {
        Config.RegisterServices(services, environment);
        return services;
    }

    /// <summary>
    /// Activation phase: mirrors IamModule.UseIamModuleAsync. This task adds
    /// only configuration validation (fail closed) and pending migrations —
    /// there is no endpoint to map and no seed step yet.
    /// </summary>
    public static async Task UseShopModuleAsync(this WebApplication app)
    {
        ValidateShopModuleConfiguration(app.Environment, app.Configuration);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.MigrateAsync();
    }

    private static void ValidateShopModuleConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        Config.ValidateConfiguration(environment, configuration);
    }
}
