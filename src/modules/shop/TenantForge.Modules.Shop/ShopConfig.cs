using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Features.Media;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop;

public sealed class ShopConfig : IModuleConfig
{
    public string SectionName => "Shop";

    internal const int DefaultCartReservationMinutes = 30;
    internal const int MinCartReservationMinutes = 5;
    internal const int MaxCartReservationMinutes = 1440;
    internal const int MinCartCleanupIntervalSeconds = 30;
    internal const int MaxCartCleanupIntervalSeconds = 3600;

    private string ShopConnectionStringPath => $"{SectionName}:ShopDb";
    private string MediaRootPath => $"{SectionName}:MediaRoot";
    private string CartReservationMinutesPath => $"{SectionName}:CartReservationMinutes";
    private string CartCleanupIntervalSecondsPath => $"{SectionName}:CartCleanupIntervalSeconds";

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

        // Scoped, not singleton: SandboxPaymentGateway depends on the scoped
        // ShopDbContext, mirroring IAMConfig.RegisterServices' own
        // scoped-vs-singleton reasoning for its database-backed services.
        services.AddScoped<Features.Payments.IShopPaymentGateway, Features.Payments.SandboxPaymentGateway>();
        services.AddScoped<IShopMediaStorage, LocalShopMediaStorage>();
        services.AddScoped<ShopImageValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IShopCartExpiryService, ShopCartExpiryService>();
        services.AddHostedService<ShopCartCleanupWorker>();

        // B035: Shop's own contribution to the shared permission catalog
        // (the second real contributor, after IAM's — see B034).
        services.AddSingleton<IPermissionCatalogContributor, ShopPermissionCatalogContributor>();
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var connectionString = configuration[ShopConnectionStringPath];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{ShopConnectionStringPath}' configuration value is required for Shop persistence.");
        }

        var mediaRoot = configuration[MediaRootPath];
        if (string.IsNullOrWhiteSpace(mediaRoot))
        {
            throw new InvalidOperationException(
                $"The '{MediaRootPath}' configuration value is required for Shop media storage.");
        }

        LocalShopMediaStorage.ValidateRoot(configuration);

        _ = GetCartReservationMinutes(environment, configuration);
        _ = GetCartCleanupIntervalSeconds(configuration);
    }

    internal static int GetCartReservationMinutes(IHostEnvironment environment, IConfiguration configuration)
    {
        var rawValue = configuration[$"Shop:CartReservationMinutes"];
        if (string.IsNullOrWhiteSpace(rawValue) && environment.IsDevelopment())
        {
            return DefaultCartReservationMinutes;
        }

        if (!int.TryParse(rawValue, out var minutes) || minutes < MinCartReservationMinutes || minutes > MaxCartReservationMinutes)
        {
            throw new InvalidOperationException(
                "The 'Shop:CartReservationMinutes' configuration value must be an integer from 5 through 1440.");
        }

        return minutes;
    }

    internal static int GetCartCleanupIntervalSeconds(IConfiguration configuration)
    {
        var rawValue = configuration[$"Shop:CartCleanupIntervalSeconds"];
        if (!int.TryParse(rawValue, out var seconds) || seconds < MinCartCleanupIntervalSeconds || seconds > MaxCartCleanupIntervalSeconds)
        {
            throw new InvalidOperationException(
                "The 'Shop:CartCleanupIntervalSeconds' configuration value must be an integer from 30 through 3600.");
        }

        return seconds;
    }
}
