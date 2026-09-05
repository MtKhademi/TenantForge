using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TenantForge.Modules.Iam.Features.Login;
using TenantForge.Modules.Iam.Infrastructure;

namespace TenantForge.Modules.Iam;

public sealed class IAMConfig : IModuleConfig
{
    public string SectionName => "IAM";

    private const string IamConnectionStringName = "IamDb";

    private string DevelopmentLoginPath => $"{SectionName}:{DevelopmentLoginOptions.SectionName}";

    private string IamConnectionStringPath => $"{SectionName}:{IamConnectionStringName}";

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

        // The options are bound lazily from the full configuration so late
        // sources (including test hosts) are honored. Outside Development the
        // section is absent by definition (startup validation forbids it), so
        // this binds an empty object and no signing key is ever available.
        services.AddSingleton(sp =>
        {
            var section = sp.GetRequiredService<IConfiguration>().GetSection(DevelopmentLoginPath);
            return section.Get<DevelopmentLoginOptions>() ?? new DevelopmentLoginOptions();
        });

        // JwtBearer validates the tokens issued by DevelopmentJwtIssuer and
        // provides the 401 challenge for protected endpoints in every
        // environment. The signing key is assigned at runtime by
        // DevelopmentJwtBearerOptions: when it is absent (outside Development)
        // token validation always fails and protected endpoints fail closed.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = DevelopmentAdminIdentity.Issuer,
                    ValidateAudience = true,
                    ValidAudience = DevelopmentAdminIdentity.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, DevelopmentJwtBearerOptions>();

        // The credential source and token issuer are the development-only login
        // shortcut; outside Development they are simply absent, so login is
        // impossible in those environments.
        if (environment.EnvironmentName != Environments.Development)
        {
            return;
        }

        services.AddSingleton<ICredentialChecker, DevelopmentCredentialChecker>();
        services.AddSingleton<DevelopmentJwtIssuer>();
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
        var connectionString = configuration[IamConnectionStringPath];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The '{IamConnectionStringPath}' configuration value is required for IAM persistence.");
        }

        var section = configuration.GetSection(DevelopmentLoginPath);
        var email = section.GetSection("Email").Value;
        var password = section.GetSection("Password").Value;
        var signingKey = section.GetSection("SigningKey").Value;
        var isConfigured = email is not null || password is not null || signingKey is not null;

        if (environment.EnvironmentName != Environments.Development)
        {
            if (isConfigured)
            {
                throw new InvalidOperationException(
                    $"The '{DevelopmentLoginPath}' section is present but the environment is '{environment.EnvironmentName}'. " +
                    "Development login credentials must never be configured outside Development.");
            }
            return;
        }

        if (email is null || password is null || signingKey is null)
        {
            throw new InvalidOperationException(
                $"The '{DevelopmentLoginPath}' section is incomplete in the Development environment. " +
                "Email, Password and SigningKey must all be configured for development login.");
        }
    }
}
