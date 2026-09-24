using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Features.Media;
using TenantForge.Modules.Shop.Features.Payments;
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

    /// <summary>
    /// B045: the explicit allowlist of ZarinPal provider-facing hosts. Only
    /// these three hosts are accepted for the request/verify/gateway URLs, and
    /// only in Development may they be non-HTTPS (local test doubles). The
    /// allowlist is host-anchored: a subdomain or suffix look-alike is not
    /// one of these hosts.
    /// </summary>
    internal static readonly string[] ZarinPalAllowedHosts =
    [
        "api.zarinpal.com",
        "checkout.zarinpal.com",
        "dev.zarinpal.com",
    ];

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

        // B045: the real ZarinPal provider behind the same gateway seam. Its
        // typed HttpClient (AddHttpClient) gets pooling/lifetime management
        // from DI rather than a hand-newed HttpClient; the per-call timeout is
        // enforced in the gateway through its own CancellationTokenSource, so
        // registration stays I/O-free and the timeout is one options value,
        // not two configuration surfaces.
        services.AddOptions<Features.Payments.ZarinPal.ZarinPalOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(Features.Payments.ZarinPal.ZarinPalOptions.SectionPath).Bind(options));
        services.AddHttpClient("ZarinPalPaymentGateway");
        services.AddScoped<Features.Payments.IShopPaymentGateway, Features.Payments.ZarinPal.ZarinPalPaymentGateway>();
        // The bound options as a first-class scoped service (the endpoint
        // handlers inject the concrete type): resolving through IOptions
        // guarantees the configuration-bound value, never a default-constructed
        // one.
        services.AddScoped<Features.Payments.ZarinPal.ZarinPalOptions>(
            serviceProvider => serviceProvider.GetRequiredService<IOptions<Features.Payments.ZarinPal.ZarinPalOptions>>().Value);
        services.AddScoped<Features.Payments.ZarinPal.ZarinPalCallbackStateProtector>();
        services.AddDataProtection();
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

        if (string.Equals(provider, Features.Payments.ZarinPal.ZarinPalPaymentGateway.ProviderName, StringComparison.Ordinal))
        {
            ValidateZarinPalConfiguration(environment, configuration);
        }
    }

    /// <summary>
    /// B045: validated only when the configured provider is <c>ZarinPal</c> —
    /// selecting Sandbox skips this entirely, matching the module's existing
    /// "only the selected provider's shape is checked" rule. Currency must be
    /// one of the two supported values. In Production every one of the five
    /// URLs must use HTTPS, and the three provider-facing hosts
    /// (<c>RequestEndpoint</c>, <c>VerifyEndpoint</c>, <c>GatewayBaseUrl</c>)
    /// must be on the exact <see cref="ZarinPalAllowedHosts"/> allowlist — a
    /// misconfigured or spoofed host fails startup rather than silently
    /// sending the merchant id and customer amounts somewhere unexpected. The
    /// merchant id itself is only checked for presence: its value is a secret
    /// and is never echoed in the exception message.
    /// </summary>
    private static void ValidateZarinPalConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var section = configuration.GetSection(Features.Payments.ZarinPal.ZarinPalOptions.SectionPath);
        var options = section.Get<Features.Payments.ZarinPal.ZarinPalOptions>() ?? new Features.Payments.ZarinPal.ZarinPalOptions();

        if (string.IsNullOrWhiteSpace(options.MerchantId))
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:MerchantId' configuration value is required.");
        }

        if (!Features.Payments.ZarinPal.ZarinPalOptions.SupportedCurrencies.Contains(options.Currency))
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:Currency' configuration value must be one of: " +
                string.Join(", ", Features.Payments.ZarinPal.ZarinPalOptions.SupportedCurrencies) + ".");
        }

        if (options.TimeoutSeconds < Features.Payments.ZarinPal.ZarinPalOptions.MinTimeoutSeconds
            || options.TimeoutSeconds > Features.Payments.ZarinPal.ZarinPalOptions.MaxTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:TimeoutSeconds' configuration value must be an integer from " +
                $"{Features.Payments.ZarinPal.ZarinPalOptions.MinTimeoutSeconds} through {Features.Payments.ZarinPal.ZarinPalOptions.MaxTimeoutSeconds}.");
        }

        RequireConfiguredUri(options.RequestEndpoint, "RequestEndpoint");
        RequireConfiguredUri(options.VerifyEndpoint, "VerifyEndpoint");
        RequireConfiguredUri(options.GatewayBaseUrl, "GatewayBaseUrl");
        RequireConfiguredUri(options.PublicApiBaseUrl, "PublicApiBaseUrl");
        RequireConfiguredUri(options.FrontendResultBaseUrl, "FrontendResultBaseUrl");

        if (!environment.IsDevelopment())
        {
            RequireHttps(options.RequestEndpoint, "RequestEndpoint");
            RequireHttps(options.VerifyEndpoint, "VerifyEndpoint");
            RequireHttps(options.GatewayBaseUrl, "GatewayBaseUrl");
            RequireHttps(options.PublicApiBaseUrl, "PublicApiBaseUrl");
            RequireHttps(options.FrontendResultBaseUrl, "FrontendResultBaseUrl");

            RequireAllowedHost(options.RequestEndpoint, "RequestEndpoint");
            RequireAllowedHost(options.VerifyEndpoint, "VerifyEndpoint");
            RequireAllowedHost(options.GatewayBaseUrl, "GatewayBaseUrl");
        }
    }

    private static void RequireConfiguredUri(Uri? value, string propertyName)
    {
        if (value is null)
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:{propertyName}' configuration value is required.");
        }
    }

    private static void RequireHttps(Uri value, string propertyName)
    {
        if (!string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:{propertyName}' configuration value must use HTTPS outside Development.");
        }
    }

    private static void RequireAllowedHost(Uri value, string propertyName)
    {
        if (!ZarinPalAllowedHosts.Contains(value.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{Features.Payments.ZarinPal.ZarinPalOptions.SectionPath}:{propertyName}' host '{value.Host}' is not on the ZarinPal allowlist.");
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
