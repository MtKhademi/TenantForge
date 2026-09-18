using TenantForge.Api;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Shop;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddIamModule(builder.Environment);
builder.Services.AddShopModule(builder.Environment);

// B034: one aggregate built from every registered IPermissionCatalogContributor
// (today: IAM's own; B035 adds Shop's). Registered after both modules'
// RegisterServices calls so every contributor is already in the container.
builder.Services.AddSingleton<IAggregatedPermissionCatalog>(sp =>
    new AggregatedPermissionCatalog(sp.GetServices<IPermissionCatalogContributor>()));

var app = builder.Build();

app.UseCors();

// IAM activation owns, in deterministic order: configuration validation
// (fail closed), authentication middleware, authorization middleware,
// pending migrations, idempotent platform-administrator seeding, and mapping
// every IAM endpoint. The host does not call any of those steps separately.
await app.UseIamModuleAsync();

// Shop activation owns: configuration validation (fail closed), pending
// migrations, and mapping every Shop endpoint (the tenant-scoped catalog
// admin routes from B026).
await app.UseShopModuleAsync();

app.MapHealth();

app.Run();

public partial class Program;
