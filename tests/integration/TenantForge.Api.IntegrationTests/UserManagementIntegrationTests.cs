using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class UserManagementIntegrationTests(IamDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private static async Task<string> LoginAsync(HttpClient client)
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

    private static async Task AuthorizeAsPlatformAdminAsync(HttpClient client)
    {
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task PlatformAdminToken_CanListSeededAdministrator_WithSafeContract()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);

        var response = await client.GetAsync("/api/platform/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var users = document.RootElement.GetProperty("users");
        Assert.Equal(JsonValueKind.Array, users.ValueKind);

        var seededAdmin = users.EnumerateArray().Single(user =>
            user.GetProperty("email").GetString() == ApiFactory.Email);

        Assert.True(Guid.TryParse(seededAdmin.GetProperty("id").GetString(), out _));
        Assert.Equal(ApiFactory.DisplayName, seededAdmin.GetProperty("displayName").GetString());
        Assert.Equal("Active", seededAdmin.GetProperty("status").GetString());
        Assert.True(seededAdmin.GetProperty("isPlatformAdmin").GetBoolean());
        Assert.True(DateTimeOffset.TryParse(seededAdmin.GetProperty("createdAtUtc").GetString(), out _));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ApiFactory.Password, body);
    }

    [Fact]
    public async Task PlatformAdminToken_CanCreateUser_AndListAfterRefresh()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);

        var email = $"member-{Guid.NewGuid():N}@tenantforge.local";
        var password = "valid-user-password";
        var createResponse = await client.PostAsJsonAsync("/api/platform/users", new
        {
            email,
            displayName = "New Team Member",
            password
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createDocument.RootElement;
        Assert.True(Guid.TryParse(created.GetProperty("id").GetString(), out _));
        Assert.Equal(email, created.GetProperty("email").GetString());
        Assert.Equal("New Team Member", created.GetProperty("displayName").GetString());
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.False(created.GetProperty("isPlatformAdmin").GetBoolean());
        Assert.True(DateTimeOffset.TryParse(created.GetProperty("createdAtUtc").GetString(), out _));

        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", createBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, createBody);

        using var refreshedClient = CreateClient();
        await AuthorizeAsPlatformAdminAsync(refreshedClient);
        var listResponse = await refreshedClient.GetAsync("/api/platform/users");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var createdInList = listDocument.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Single(user => user.GetProperty("email").GetString() == email);

        Assert.Equal(created.GetProperty("id").GetString(), createdInList.GetProperty("id").GetString());
        Assert.False(createdInList.GetProperty("isPlatformAdmin").GetBoolean());
    }

    [Fact]
    public async Task InvalidCreateRequest_Returns400ValidationProblem()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/platform/users", new
        {
            email = "not-an-email",
            displayName = " ",
            password = "short"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("displayName", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task DuplicateEmail_Returns409StableProblemWithoutInternals()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/platform/users", new
        {
            email = ApiFactory.Email.ToUpperInvariant(),
            displayName = "Duplicate Admin",
            password = "valid-duplicate-password"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate email", body);
        Assert.Contains("already exists", body);
        Assert.DoesNotContain("ix_iam_accounts_normalized_email", body);
        Assert.DoesNotContain("DbUpdateException", body);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401ForListAndCreate()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/platform/users", new
        {
            email = "unauthorized@tenantforge.local",
            displayName = "Unauthorized",
            password = "valid-password"
        })).StatusCode);
    }

    [Fact]
    public async Task ValidTokenWithoutPlatformAdminClaim_Returns403ForListAndCreate()
    {
        using var client = CreateClient();
        var nonAdminToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey, isPlatformAdmin: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonAdminToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/platform/users", new
        {
            email = "forbidden@tenantforge.local",
            displayName = "Forbidden",
            password = "valid-password"
        })).StatusCode);
    }
}
