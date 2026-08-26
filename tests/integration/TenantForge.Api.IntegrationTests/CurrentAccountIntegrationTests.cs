using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public class CurrentAccountIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", developmentLoginMode: DevelopmentLoginMode.Complete);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task ValidDevelopmentToken_Returns200_WithExactCurrentAccountContract()
    {
        using var client = CreateClient();
        var accessToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("development-admin", root.GetProperty("id").GetString());
        Assert.Equal(ApiFactory.Email, root.GetProperty("email").GetString());
        Assert.Equal("Platform Administrator", root.GetProperty("displayName").GetString());
        Assert.True(root.GetProperty("isPlatformAdmin").GetBoolean());
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MalformedBearerToken_Returns401()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        using var client = CreateClient();

        // Same identity, issuer, audience and valid HS256 signature as a real
        // development token, but exp is in the past (beyond the 30s clock skew).
        // The handler refuses this token purely because of lifetime validation.
        var expiredToken = TestJwtFactory.IssueExpired(signingKey: ApiFactory.SigningKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithWrongKey_Returns401()
    {
        using var client = CreateClient();

        // Well-formed claims, but the signature was produced by a different key,
        // so the identity is provably not the server's.
        const string wrongKey = "a-different-signing-key-long-enough-32b!!";
        var forgedToken = TestJwtFactory.Issue(signingKey: wrongKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithWrongIssuerOrAudience_Returns401()
    {
        using var client = CreateClient();

        var wrongIssuer = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey, issuer: "AnotherApp");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongIssuer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey, audience: "AnotherApp"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ProductionWithoutDevelopmentSection_MeEndpointFailsClosedWith401()
    {
        using var productionFactory = new ApiFactory(environment: "Production", developmentLoginMode: DevelopmentLoginMode.Absent);
        using var client = productionFactory.CreateClient();

        // No Authorization header -> 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // Even a valid-looking development token is useless in Production: the
        // JWT scheme is registered but has no development signing key there, so
        // signature validation fails and the endpoint must answer 401 (never
        // 500) and never trust client-provided identity.
        var devToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}

internal static class TestJwtFactory
{
    public static string Issue(string signingKey, string issuer = "TenantForge", string audience = "TenantForge", bool isPlatformAdmin = true)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, "development-admin"),
                new Claim(JwtRegisteredClaimNames.Email, ApiFactory.Email),
                new Claim(JwtRegisteredClaimNames.Name, "Platform Administrator"),
                new Claim("isPlatformAdmin", isPlatformAdmin ? "true" : "false")
            ]),
            Audience = audience,
            Issuer = issuer,
            Expires = DateTimeOffset.UtcNow.AddMinutes(30).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    /// <summary>
    /// Builds a token whose signature, claims, issuer and audience all look valid,
    /// but whose exp (and nbf) are in the past. SecurityTokenDescriptor is not used
    /// here because it pins nbf to "now", which would make the token internally
    /// inconsistent (exp before nbf) instead of expired.
    /// </summary>
    public static string IssueExpired(string signingKey)
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-30);
        var expires = DateTimeOffset.UtcNow.AddMinutes(-5);
        var token = new JwtSecurityToken(
            issuer: "TenantForge",
            audience: "TenantForge",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "development-admin"),
                new Claim(JwtRegisteredClaimNames.Email, ApiFactory.Email),
                new Claim(JwtRegisteredClaimNames.Name, "Platform Administrator"),
                new Claim("isPlatformAdmin", "true")
            },
            notBefore: notBefore.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
