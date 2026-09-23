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

    /// <summary>
    /// B044: the exact, closed set of payment providers the configuration
    /// accepts. The value is matched case-sensitively against a gateway's
    /// <c>IShopPaymentGateway.Provider</c>, so these strings are the wire
    /// values, not arbitrary aliases.
    /// </summary>
    internal static readonly string[] SupportedPaymentProviders =
        [Features.Payments.SandboxPaymentGateway.ProviderName, "ZarinPal"];

    private string ShopConnectionStringPath => $"{SectionName}:ShopDb";
    private string MediaRootPath => $"{SectionName}:MediaRoot";
    private string CartReservationMinutesPath => $"{SectionName}:CartReservationMinutes";
    private string CartCleanupIntervalSecondsPath => $"{SectionName}:CartCleanupIntervalSeconds";

    internal const string PaymentsProviderPath = "Shop:Payments:Provider";

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

        // Scoped, not singleton: both payment gateways and the completion
        // service depend on the scoped ShopDbContext, mirroring
        // IAMConfig.RegisterServices' own scoped-vs-singleton reasoning for
        // its database-backed services.
        //
        // B044: BOTH registered gateways (the sandbox today; ZarinPal lands
        // in B045 behind the same seam) are registered, and the one actually
        // used is picked by ShopPaymentGatewayResolver from the
        // Shop:Payments:Provider value — never by registration order.
        services.AddScoped<Features.Payments.IShopPaymentGateway, Features.Payments.SandboxPaymentGateway>();
        services.AddScoped<Features.Payments.IShopPaymentGatewayResolver, Features.Payments.ShopPaymentGatewayResolver>();
        services.AddScoped<Features.Payments.ShopPaymentCompletionService>();
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

        ValidatePaymentsProvider(environment, configuration);
    }

    /// <summary>
    /// B044: <c>Shop:Payments:Provider</c> accepts exactly the strings
    /// <c>Sandbox</c> or <c>ZarinPal</c> and nothing else. In Development an
    /// absent value defaults to <c>Sandbox</c> (keeping the repository
    /// demoable without a payments section); outside Development the value
    /// is required, and <c>Sandbox</c> is refused — the browser-driven
    /// payment simulation fails closed in Production rather than being
    /// enabled silently. A provider name that no registered gateway matches
    /// (e.g. ZarinPal before B045 registers its gateway) fails closed at the
    /// first resolution, in the resolver, not here.
    /// </summary>
    private static void ValidatePaymentsProvider(IHostEnvironment environment, IConfiguration configuration)
    {
        var provider = configuration[PaymentsProviderPath];
        if (string.IsNullOrWhiteSpace(provider))
        {
            if (environment.IsDevelopment())
            {
                return; // the Development default is Sandbox
            }

            throw new InvalidOperationException(
                $"The '{PaymentsProviderPath}' configuration value is required and must be one of: {string.Join(", ", SupportedPaymentProviders)}.");
        }

        if (!SupportedPaymentProviders.Contains(provider))
        {
            throw new InvalidOperationException(
                $"The '{PaymentsProviderPath}' configuration value must be one of: {string.Join(", ", SupportedPaymentProviders)}.");
        }

        if (!environment.IsDevelopment()
            && string.Equals(provider, Features.Payments.SandboxPaymentGateway.ProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The 'Sandbox' payment provider is not available outside Development. " +
                $"Configure '{PaymentsProviderPath}' to a real provider.");
        }
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
