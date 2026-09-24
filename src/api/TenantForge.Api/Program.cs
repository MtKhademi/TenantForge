using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using TenantForge.Api;
using TenantForge.BuildingBlocks.Permissions;
using TenantForge.Modules.Iam;
using TenantForge.Modules.Shop;
using TenantForge.Modules.Shop.Features.RateLimiting;

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

// B046: the host's one — and only — AddRateLimiter call, contributed by the
// Shop module. It partitions buckets by normalized tenant ID + effective
// remote IP (the value UseForwardedHeaders resolves, honoring the trusted-
// proxy allowlist), rejects over-limit requests immediately (queue length
// zero) and answers them with the one generic 429. Placed after both modules'
// registrations and before builder.Build() per this task's Spec.
builder.Services.AddShopRateLimiter();

// B046: the explicit trusted-proxy allowlist for forwarded headers. Bound from
// the "ForwardedHeaders" configuration section (empty by default, i.e. no
// proxy is trusted — the direct remote IP is always used). A request not
// arriving through a listed proxy/network has its forwarded headers ignored.
builder.Services.Configure<ForwardedHeadersOptions>(
    builder.Configuration.GetSection("ForwardedHeaders"));

var app = builder.Build();

// B046: the first middleware in the pipeline. Runs before UseCors so that the
// connection's RemoteIpAddress (and therefore the rate-limit partition key)
// reflects the value UseForwardedHeaders has resolved — honoring the trusted-
// proxy allowlist — rather than the raw, client-supplied X-Forwarded-For.
app.UseForwardedHeaders();

app.UseCors();

// B046: the rate-limiter pipeline middleware. Must run before the module
// activation below maps the endpoints, so every endpoint's
// RequireRateLimiting metadata is already attached by the time a request
// reaches it.
app.UseRateLimiter();

// B046: reject an over-limit Shop request body (a Content-Length above the
// bound) with a generic 413 before any model-binding or validation work runs.
// JSON Shop bodies are capped at MaxRequestBodyBytes; anything else — including
// the multipart media upload — is capped at the 5 MiB hard ceiling.
app.UseShopRequestBodySizeLimit();

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
