using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public class ProductionFailClosedTests
{
    [Fact]
    public void DevelopmentLoginSectionOutsideDevelopment_FailsStartup()
    {
        using var factory = new ApiFactory(environment: "Production", developmentLoginMode: DevelopmentLoginMode.Complete);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });
    }

    [Fact]
    public async Task ProductionWithoutDevelopmentSection_StartsAndLoginIsRejected()
    {
        using var factory = new ApiFactory(environment: "Production", developmentLoginMode: DevelopmentLoginMode.Absent);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void IncompleteDevelopmentLoginSection_FailsStartupInDevelopment()
    {
        using var factory = new ApiFactory(environment: "Development", developmentLoginMode: DevelopmentLoginMode.Partial);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
