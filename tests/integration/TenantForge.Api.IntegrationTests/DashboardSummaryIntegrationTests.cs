using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class DashboardSummaryIntegrationTests(IamDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

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
    public async Task PlatformAdminToken_Returns200_WithExactDashboardSummaryContract()
    {
        using var client = CreateClient();
        var accessToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var generatedBeforeUtc = DateTimeOffset.UtcNow;
        var response = await client.GetAsync("/api/platform/dashboard-summary");
        var generatedAfterUtc = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Development", root.GetProperty("environment").GetString());
        Assert.Equal("Healthy", root.GetProperty("apiStatus").GetString());
        // The shared fixture seeds exactly one active platform administrator;
        // the count is a real query result, not a constant.
        Assert.Equal(1, root.GetProperty("platformAdminCount").GetInt32());

        // generatedAtUtc must be a real UTC instant produced by this request,
        // not a fabricated or timezone-naive value. (Asserting on the parsed
        // offset, not DateTime.Kind: DateTimeOffset.DateTime always reports
        // Unspecified regardless of the source's zone designator.)
        var generatedAtUtc = DateTimeOffset.Parse(root.GetProperty("generatedAtUtc").GetString()!);
        Assert.Equal(TimeSpan.Zero, generatedAtUtc.Offset);
        Assert.InRange(generatedAtUtc, generatedBeforeUtc.AddSeconds(-1), generatedAfterUtc.AddSeconds(5));
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/platform/dashboard-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidTokenWithoutPlatformAdminClaim_Returns403()
    {
        using var client = CreateClient();

        // A token with a valid signature, issuer and audience, but an
        // isPlatformAdmin claim of "false": the caller IS authenticated, so
        // the authorization policy must answer 403 (forbidden), not 401.
        var nonAdminToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey, isPlatformAdmin: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonAdminToken);

        var response = await client.GetAsync("/api/platform/dashboard-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProductionWithoutSeedOrKey_DashboardSummaryFailsClosedWith401()
    {
        using var productionFactory = new ApiFactory(environment: "Production", seedMode: IamSeedMode.Absent, db);
        using var client = productionFactory.CreateClient();

        // No Authorization header -> 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/dashboard-summary")).StatusCode);

        // Even a valid-looking token signed with the known development key is
        // useless in Production: the JWT scheme has no signing key there, so
        // signature validation fails and the endpoint must answer 401 (never 500).
        var devToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/dashboard-summary")).StatusCode);
    }
}
