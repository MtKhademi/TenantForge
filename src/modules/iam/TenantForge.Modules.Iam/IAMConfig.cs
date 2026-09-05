using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Login;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam;

public sealed class IAMConfig : IModuleConfig
{
    public string SectionName => "IAM";

    private string IamConnectionStringPath => $"{SectionName}:IamDb";
    private string AuthPath => $"{SectionName}:{AuthOptions.SectionName}";
    private string SeedAdminPath => $"{SectionName}:{SeedAdminOptions.SectionName}";

    public void RegisterServices(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDbContext<IamDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()[IamConnectionStringPath];
            options.UseNpgsql(connectionString);
        });

        // Authorization services are environment-independent: protected endpoints
        // (RequireAuthorization) need them in every environment. The
        // PlatformAdmin policy requires an authenticated principal whose
        // isPlatformAdmin claim is exactly "true" (a missing/false claim is
        // authenticated-but-forbidden, so authorization middleware answers 403
        // instead of the endpoint body having to re-check and default-deny).
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicyNames.PlatformAdmin, policy =>
                policy.RequireClaim("isPlatformAdmin", "true"));
        });

        // Auth and seed options are bound lazily from the full configuration so
        // late sources (including test hosts) are honored.
        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection(AuthPath).Get<AuthOptions>() ?? new AuthOptions());
        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection(SeedAdminPath).Get<SeedAdminOptions>() ?? new SeedAdminOptions());

        // Password hashing. PasswordHasher<TUser> ships in the ASP.NET Core shared
        // framework; it stores a salted, iterated PBKDF2 hash and never the
        // plaintext password.
        services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();

        // JwtBearer validates the tokens issued by JwtIssuer and provides the 401
        // challenge for protected endpoints in every environment. The signing key
        // is assigned at runtime by JwtBearerSigningKeyOptions: when it is absent
        // a random, never-matching key is used, so no token can validate and
        // protected endpoints fail closed with 401 instead of throwing a 500.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = JwtConstants.Issuer,
                    ValidateAudience = true,
                    ValidAudience = JwtConstants.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, JwtBearerSigningKeyOptions>();

        // Database-backed authentication and the idempotent seeder are real
        // capabilities used in every environment, so they are registered
        // unconditionally. Whether login can succeed depends on configuration
        // (a seeded admin + a signing key), not on the environment name.
        // Scoped, not Singleton: both depend on the scoped IamDbContext, and a
        // singleton must never hold a scoped service (DI validates this at
        // startup and refuses to build otherwise).
        services.AddScoped<ICredentialChecker, AccountCredentialChecker>();
        services.AddScoped<PlatformAdminSeeder>();
        // JwtIssuer only depends on the singleton AuthOptions, so it stays a
        // singleton.
        services.AddSingleton<JwtIssuer>();
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var connectionString = configuration[IamConnectionStringPath];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{IamConnectionStringPath}' configuration value is required for IAM persistence.");
        }

        // The seed section must be either fully present or fully absent. A
        // partially-filled section is a misconfiguration in ANY environment and
        // fails startup. The message deliberately does not echo any configured
        // value, so a password or other secret is never exposed on the error.
        var email = configuration[$"{SeedAdminPath}:Email"];
        var password = configuration[$"{SeedAdminPath}:Password"];
        var displayName = configuration[$"{SeedAdminPath}:DisplayName"];
        var presentCount =
            (email is null || string.IsNullOrWhiteSpace(email) ? 0 : 1)
            + (password is null || string.IsNullOrWhiteSpace(password) ? 0 : 1)
            + (displayName is null || string.IsNullOrWhiteSpace(displayName) ? 0 : 1);

        if (presentCount is > 0 and < 3)
        {
            throw new InvalidOperationException(
                $"The '{SeedAdminPath}' section is incomplete; Email, Password and DisplayName must all be configured together.");
        }

        // Outside Development, an obviously weak seed password is refused. The
        // length is checked only — never the value itself — so no secret is
        // exposed by this validation.
        if (environment.EnvironmentName != Environments.Development
            && presentCount == 3
            && password!.Length < MinimumProductionSeedPasswordLength)
        {
            throw new InvalidOperationException(
                $"The '{SeedAdminPath}:Password' configuration value is unsafe for a non-Development environment: " +
                $"it must be at least {MinimumProductionSeedPasswordLength} characters.");
        }
    }

    private const int MinimumProductionSeedPasswordLength = 12;
}
