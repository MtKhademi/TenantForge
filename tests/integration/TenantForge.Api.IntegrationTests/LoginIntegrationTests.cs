using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public class LoginIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", developmentLoginMode: DevelopmentLoginMode.Complete);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    public async Task ValidDevelopmentCredentials_Return200_WithSignedTokenAndExactContract()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("accessToken").ValueKind);
        var accessToken = root.GetProperty("accessToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var expiresAtUtc = DateTimeOffset.Parse(root.GetProperty("expiresAtUtc").GetString()!);
        Assert.True(expiresAtUtc > DateTimeOffset.UtcNow, "expiresAtUtc must be in the future");

        var user = root.GetProperty("user");
        Assert.Equal("development-admin", user.GetProperty("id").GetString());
        Assert.Equal(ApiFactory.Email, user.GetProperty("email").GetString());
        Assert.Equal("Platform Administrator", user.GetProperty("displayName").GetString());
        Assert.True(user.GetProperty("isPlatformAdmin").GetBoolean());
    }

    [Fact]
    public async Task ValidDevelopmentCredentials_ReturnsJwtWithOnlyTheRequiredClaims()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = document.RootElement.GetProperty("accessToken").GetString()!;

        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap = new Dictionary<string, string>();
        var jwt = handler.ReadJwtToken(accessToken);

        Assert.Equal("development-admin", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(ApiFactory.Email, jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal("Platform Administrator", jwt.Claims.Single(c => c.Type == "name").Value);
        Assert.Equal("true", jwt.Claims.Single(c => c.Type == "isPlatformAdmin").Value);
        Assert.True(jwt.ValidTo > DateTime.UtcNow, "token must not be expired");
    }

    [Fact]
    public async Task WrongPassword_Returns401GenericProblemDetails_WithoutLeakingCredentials()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = "definitely-not-the-right-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiFactory.Password, body);
        Assert.Contains("Invalid credentials", body);
    }

    [Fact]
    public async Task WrongEmail_ReturnsIdentical401ProblemDetails_AsWrongPassword()
    {
        using var client = CreateClient();

        var wrongEmail = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "someone-else@tenantforge.local",
            password = ApiFactory.Password
        });
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = "a-different-wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var wrongEmailBody = await wrongEmail.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();

        Assert.Equal(ExtractTitle(wrongPasswordBody), ExtractTitle(wrongEmailBody));
        Assert.Equal(ExtractDetail(wrongPasswordBody), ExtractDetail(wrongEmailBody));
    }

    [Fact]
    public async Task MissingFields_Returns400Not500()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task PasswordValueNeverAppearsInLogOutput()
    {
        using var client = CreateClient();
        _factory.LogCollector.Clear();

        await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = "super-secret-attempt-12345"
        });

        Assert.NotEmpty(_factory.LogCollector.Entries);
        foreach (var entry in _factory.LogCollector.Entries)
        {
            Assert.DoesNotContain("super-secret-attempt-12345", entry);
            Assert.DoesNotContain(ApiFactory.Password, entry);
        }
    }

    [Fact]
    public async Task HealthEndpoint_Returns200Ok()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    private static string ExtractTitle(string problemDetailsBody)
    {
        using var document = JsonDocument.Parse(problemDetailsBody);
        return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
    }

    private static string ExtractDetail(string problemDetailsBody)
    {
        using var document = JsonDocument.Parse(problemDetailsBody);
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? string.Empty : string.Empty;
    }
}
