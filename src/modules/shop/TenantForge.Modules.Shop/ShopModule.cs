using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Features.Categories;
using TenantForge.Modules.Shop.Features.Media;
using TenantForge.Modules.Shop.Features.Products;
using TenantForge.Modules.Shop.Features.Storefront;
using TenantForge.Modules.Shop.Features.Checkout;
using TenantForge.Modules.Shop.Features.Coupons;
using TenantForge.Modules.Shop.Features.Orders;
using TenantForge.Modules.Shop.Features.Payments;
using TenantForge.Modules.Shop.Features.Shipping;
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
    /// Activation phase: mirrors IamModule.UseIamModuleAsync. Owns, in
    /// deterministic order: configuration validation (fail closed), pending
    /// migrations, and mapping every Shop endpoint. There is no seed step
    /// (B025 decided against seed data for the catalog).
    /// </summary>
    public static async Task UseShopModuleAsync(this WebApplication app)
    {
        ValidateShopModuleConfiguration(app.Environment, app.Configuration);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.MigrateAsync();

        MapShopModule(app);
    }

    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
        endpoints.MapProductMediaFeature();
        endpoints.MapStorefrontCatalogFeature();
        endpoints.MapShippingRatesFeature();
        endpoints.MapCouponsFeature();
        endpoints.MapCartsFeature();
        endpoints.MapCheckoutFeature();
        endpoints.MapOrderCreationFeature();
        endpoints.MapPaymentsFeature();
        endpoints.MapOrderLookupFeature();
    }

    private static void ValidateShopModuleConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        Config.ValidateConfiguration(environment, configuration);
    }
}
