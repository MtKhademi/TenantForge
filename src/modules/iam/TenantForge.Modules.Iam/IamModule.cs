using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TenantForge.BuildingBlocks.Modules;
using TenantForge.Modules.Iam.Features.Account;
using TenantForge.Modules.Iam.Features.Audit;
using TenantForge.Modules.Iam.Features.Dashboard;
using TenantForge.Modules.Iam.Features.Invitations;
using TenantForge.Modules.Iam.Features.Login;
using TenantForge.Modules.Iam.Features.Roles;
using TenantForge.Modules.Iam.Features.TenantMembers;
using TenantForge.Modules.Iam.Features.Tenants;
using TenantForge.Modules.Iam.Features.Users;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam;

public static class IamModule
{
    private static readonly IModuleConfig Config = new IAMConfig();

    /// <summary>
    /// Registration phase: adds every IAM-owned service to the container.
    /// Runs before <c>builder.Build()</c> and performs no I/O and no pass/fail
    /// decision — <see cref="IModuleConfig.RegisterServices"/> only registers
    /// what IAM might need; it must not read configuration values that are not
    /// yet guaranteed to be fully assembled (host and test-host sources alike).
    /// </summary>
    public static IServiceCollection AddIamModule(this IServiceCollection services, IHostEnvironment environment)
    {
        Config.RegisterServices(services, environment);
        return services;
    }

    /// <summary>
    /// Activation phase: everything IAM must do to a built application before
    /// it can safely serve traffic. Runs once, after <c>builder.Build()</c>, in
    /// this deterministic order: validate the fully assembled configuration
    /// (fail closed), install authentication then authorization middleware,
    /// apply pending migrations, seed the platform administrator idempotently,
    /// then map every IAM endpoint. The host calls only this one method; it
    /// must not reach into any of these steps individually.
    /// </summary>
    public static async Task UseIamModuleAsync(this WebApplication app)
    {
        ValidateIamModuleConfiguration(app.Environment, app.Configuration);

        // Authentication and authorization run in every environment. The JWT
        // scheme is registered by AddIamModule unconditionally, but outside
        // Development its signing key does not exist (validation above forbids
        // that), so token validation always fails and protected endpoints
        // answer 401 — fail closed.
        app.UseAuthentication();
        app.UseAuthorization();

        await SeedIamModuleAsync(app.Services);

        MapIamModule(app);
    }

    private static void ValidateIamModuleConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        Config.ValidateConfiguration(environment, configuration);
    }

    /// <summary>
    /// Applies any pending IAM migrations and seeds the platform administrator
    /// (idempotently). Called once at startup after configuration validation,
    /// before the host starts serving requests. Runs in a scope so the scoped
    /// DbContext and seeder are resolved and disposed correctly.
    /// </summary>
    private static async Task SeedIamModuleAsync(IServiceProvider services)
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

    private static void MapIamModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapLoginFeature();
        endpoints.MapCurrentAccountFeature();
        endpoints.MapTenantDiscoveryFeature();
        endpoints.MapDashboardSummaryFeature();
        endpoints.MapUsersFeature();
        endpoints.MapTenantsFeature();
        endpoints.MapTenantMembersFeature();
        endpoints.MapRolesFeature();
        endpoints.MapInvitationsFeature();
        endpoints.MapAuditFeature();
    }
}
