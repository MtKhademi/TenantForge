using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop;

public sealed class ShopConfig : IModuleConfig
{
    public string SectionName => "Shop";

    private string ShopConnectionStringPath => $"{SectionName}:ShopDb";

    public void RegisterServices(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDbContext<ShopDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()[ShopConnectionStringPath];
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                // Shop:ShopDb points at the same physical database as
                // IAM:IamDb (see this Spec's Context section). Each module's
                // migration history must be tracked in its own table or the
                // two modules' migration histories collide.
                npgsqlOptions.MigrationsHistoryTable("__ShopMigrationsHistory");
            });
        });
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var connectionString = configuration[ShopConnectionStringPath];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{ShopConnectionStringPath}' configuration value is required for Shop persistence.");
        }
    }
}
