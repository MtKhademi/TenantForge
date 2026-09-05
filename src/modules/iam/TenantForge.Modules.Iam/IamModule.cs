using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenantForge.Modules.Iam.Features.Account;
using TenantForge.Modules.Iam.Features.Dashboard;
using TenantForge.Modules.Iam.Features.Login;
using TenantForge.Modules.Iam.Features.Users;
using TenantForge.Modules.Iam.Infrastructure;

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

    /// <summary>
    /// Applies any pending IAM migrations and seeds the platform administrator
    /// (idempotently). Called once at startup after configuration validation,
    /// before the host starts serving requests. Runs in a scope so the scoped
    /// DbContext and seeder are resolved and disposed correctly.
    /// </summary>
    public static async Task SeedIamModuleAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>();
        var seedOptions = scope.ServiceProvider.GetRequiredService<SeedAdminOptions>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TenantForge.Modules.Iam.Seeding");

        // Migrations first, so the iam_accounts table exists before we seed.
        await db.Database.MigrateAsync();

        if (!seedOptions.IsConfigured)
        {
            logger.LogInformation(
                "IAM:SeedAdmin is not configured; skipping platform administrator seeding.");
            return;
        }

        var created = await seeder.SeedAsync(seedOptions, DateTimeOffset.UtcNow);
        // Log only the email (an identity, not a secret), never the password or
        // the resulting hash.
        logger.LogInformation(
            "Platform administrator seeding {Outcome} for {Email}.",
            created ? "created" : "already-present",
            seedOptions.Email);
    }

    public static IEndpointRouteBuilder MapIamModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLoginFeature();
        endpoints.MapCurrentAccountFeature();
        endpoints.MapDashboardSummaryFeature();
        endpoints.MapUsersFeature();
        return endpoints;
    }
}
