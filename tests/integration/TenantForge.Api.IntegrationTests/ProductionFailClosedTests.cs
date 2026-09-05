using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class ProductionFailClosedTests(IamDbFixture db)
{
    [Fact]
    public async Task ProductionWithoutSeedOrSigningKey_StartsAndLoginIsRejected()
    {
        using var factory = new ApiFactory(environment: "Production", seedMode: IamSeedMode.Absent, db);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void IncompleteSeedAdminSection_FailsStartup()
    {
        using var factory = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Partial, db);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("incomplete", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeShortSeedPasswordOutsideDevelopment_FailsStartup()
    {
        using var factory = new UnsafeSeedApiFactory(db);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);
        // The actual (short) password value must never appear in the exception.
        Assert.DoesNotContain("short", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoSigningKeyConfigured_LoginFailsClosedWith401_EvenWithValidSeededAccount()
    {
        using var factory = new ApiFactory(environment: "Development", seedMode: IamSeedMode.NoSigningKey, db);
        using var client = factory.CreateClient();

        // The seeded admin exists and the password is correct, but no signing
        // key is configured: the endpoint must refuse to mint an unverifiable
        // token rather than throwing a 500.
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>
/// A Production host with a SeedAdmin section that is complete but carries an
/// obviously unsafe (too short) password, used only to prove that startup
/// validation refuses it.
/// </summary>
internal sealed class UnsafeSeedApiFactory(IamDbFixture db) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-iam-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Production");

        var values = new Dictionary<string, string?>
        {
            ["IAM:IamDb"] = db.ConnectionString,
            ["IAM:Auth:SigningKey"] = "some-signing-key-long-enough-32bytes!!",
            ["IAM:SeedAdmin:Email"] = "unsafe-admin@tenantforge.local",
            ["IAM:SeedAdmin:Password"] = "short",
            ["IAM:SeedAdmin:DisplayName"] = "Unsafe Admin"
        };

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));
    }
}
