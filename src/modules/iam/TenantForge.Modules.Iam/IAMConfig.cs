using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantForge.Modules.Iam.Features.Login;

namespace TenantForge.Modules.Iam;

public sealed class IAMConfig : IModuleConfig
{
    public string SectionName => "IAM";

    private string DevelopmentLoginPath => $"{SectionName}:{DevelopmentLoginOptions.SectionName}";

    public void RegisterServices(IServiceCollection services, IHostEnvironment environment)
    {
        if (environment.EnvironmentName != Environments.Development)
        {
            return;
        }

        services.AddSingleton(sp =>
        {
            var section = sp.GetRequiredService<IConfiguration>().GetSection(DevelopmentLoginPath);
            return section.Get<DevelopmentLoginOptions>() ?? new DevelopmentLoginOptions();
        });
        services.AddSingleton<ICredentialChecker, DevelopmentCredentialChecker>();
        services.AddSingleton<DevelopmentJwtIssuer>();
    }

    public void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration)
    {
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
