using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TenantForge.Modules.Iam;

public interface IModuleConfig
{
    string SectionName { get; }

    void RegisterServices(IServiceCollection services, IHostEnvironment environment);

    void ValidateConfiguration(IHostEnvironment environment, IConfiguration configuration);
}
