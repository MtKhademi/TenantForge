using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class TenantMembershipIntegrationTests(IamDbFixture db) : IDisposable
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

    private async Task<Guid> CreateOwnerUserAsync(string email)
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var account = Account.CreateUser(email, "Tenant Owner", "already-hashed-for-test", now);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task PlatformAdmin_CanCreateTenantWithFirstOwner_AndReloadIt()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);
        var ownerUserId = await CreateOwnerUserAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var slug = $"acme-{Guid.NewGuid():N}"[..18];

        var createResponse = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = " Acme Property ",
            slug = slug.ToUpperInvariant(),
            ownerUserId
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var created = createDocument.RootElement;
        var tenantId = created.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, tenantId);
        Assert.Equal("Acme Property", created.GetProperty("name").GetString());
        Assert.Equal(slug, created.GetProperty("slug").GetString());
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.Equal(1, created.GetProperty("memberCount").GetInt32());
        Assert.True(DateTimeOffset.TryParse(created.GetProperty("createdAtUtc").GetString(), out _));

        await using (var context = db.CreateContext())
        {
            var membership = await context.TenantMemberships.SingleAsync(m => m.TenantId == tenantId);
            Assert.Equal(ownerUserId, membership.AccountId);
            Assert.Equal(TenantMembershipRole.Owner, membership.Role);
        }

        using var refreshedClient = CreateClient();
        await AuthorizeAsPlatformAdminAsync(refreshedClient);
        var listResponse = await refreshedClient.GetAsync("/api/platform/tenants");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var createdInList = listDocument.RootElement
            .GetProperty("tenants")
            .EnumerateArray()
            .Single(tenant => tenant.GetProperty("id").GetGuid() == tenantId);

        Assert.Equal("Acme Property", createdInList.GetProperty("name").GetString());
        Assert.Equal(slug, createdInList.GetProperty("slug").GetString());
        Assert.Equal(1, createdInList.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task InvalidOrMissingOwner_Returns400_AndCreatesNoPartialTenant()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);
        var missingOwnerSlug = $"missing-owner-{Guid.NewGuid():N}"[..24];
        var unknownOwnerSlug = $"unknown-owner-{Guid.NewGuid():N}"[..24];

        var missingOwnerResponse = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Missing Owner Tenant",
            slug = missingOwnerSlug,
            ownerUserId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, missingOwnerResponse.StatusCode);
        using (var document = JsonDocument.Parse(await missingOwnerResponse.Content.ReadAsStringAsync()))
        {
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("ownerUserId", out _));
        }

        var unknownOwnerResponse = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Unknown Owner Tenant",
            slug = unknownOwnerSlug,
            ownerUserId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, unknownOwnerResponse.StatusCode);
        using (var document = JsonDocument.Parse(await unknownOwnerResponse.Content.ReadAsStringAsync()))
        {
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("ownerUserId", out _));
        }

        await using var context = db.CreateContext();
        Assert.False(await context.Tenants.AnyAsync(tenant =>
            tenant.NormalizedSlug == missingOwnerSlug || tenant.NormalizedSlug == unknownOwnerSlug));
    }

    [Fact]
    public async Task DuplicateSlug_Returns409StableProblem_AndCreatesNoExtraMembership()
    {
        using var client = CreateClient();
        await AuthorizeAsPlatformAdminAsync(client);
        var firstOwnerUserId = await CreateOwnerUserAsync($"first-owner-{Guid.NewGuid():N}@tenantforge.local");
        var secondOwnerUserId = await CreateOwnerUserAsync($"second-owner-{Guid.NewGuid():N}@tenantforge.local");
        var slug = $"duplicate-{Guid.NewGuid():N}"[..22];

        var firstResponse = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "First Tenant",
            slug,
            ownerUserId = firstOwnerUserId
        });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateResponse = await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Duplicate Tenant",
            slug = slug.ToUpperInvariant(),
            ownerUserId = secondOwnerUserId
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("application/problem+json", duplicateResponse.Content.Headers.ContentType!.MediaType);

        var body = await duplicateResponse.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate tenant slug", body);
        Assert.Contains("already exists", body);
        Assert.DoesNotContain("ix_iam_tenants_normalized_slug", body);
        Assert.DoesNotContain("DbUpdateException", body);

        await using var context = db.CreateContext();
        var tenantIds = await context.Tenants
            .Where(tenant => tenant.NormalizedSlug == slug)
            .Select(tenant => tenant.Id)
            .ToListAsync();
        Assert.Single(tenantIds);
        Assert.Equal(1, await context.TenantMemberships.CountAsync(membership => membership.TenantId == tenantIds[0]));
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401ForListAndCreate()
    {
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Unauthorized Tenant",
            slug = "unauthorized-tenant",
            ownerUserId = Guid.NewGuid()
        })).StatusCode);
    }

    [Fact]
    public async Task ValidTokenWithoutPlatformAdminClaim_Returns403ForListAndCreate()
    {
        using var client = CreateClient();
        var nonAdminToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey, isPlatformAdmin: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonAdminToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/platform/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/platform/tenants", new
        {
            name = "Forbidden Tenant",
            slug = "forbidden-tenant",
            ownerUserId = Guid.NewGuid()
        })).StatusCode);
    }
}
