using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// S11 tenant discovery contract for F018: GET /api/auth/me/tenants returns
/// exactly the caller's active-tenant memberships, ordered by name then id,
/// with no admin bypass.
///
/// Runs in its own <see cref="TenantDiscoveryIsolatedCollection"/> (a separate
/// PostgreSQL database) because these tests create several accounts per case.
/// Sharing the main <see cref="IamApiTestCollection"/> pool would inflate the
/// account count that GET /api/platform/users' oldest-first 50-row page depends
/// on in other classes, so discovery is kept hermetic to that pool.
/// </summary>
[Collection(nameof(TenantDiscoveryIsolatedCollection))]
public class TenantDiscoveryIntegrationTests(TenantDiscoveryDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private static void Authorize(HttpClient client, Guid accountId, bool isPlatformAdmin = false)
    {
        var token = TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            isPlatformAdmin: isPlatformAdmin,
            subject: accountId.ToString(),
            email: $"{accountId:N}@tenantforge.local",
            displayName: "Discovery Account");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Creates one account (persisted first so its generated id is known),
    /// then the tenant layout around it. The account id is returned so the
    /// JWT's "sub" claim matches the persisted row exactly.
    /// </summary>
    private async Task<DiscoveryFixture> ArrangeTenantsAsync()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;

        var account = Account.CreateUser($"disc-{Guid.NewGuid():N}@tenantforge.local", "Discovery Caller", "hash", now);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        var accountId = account.Id;

        // Two active tenants the caller belongs to, plus a third active tenant
        // the caller does not belong to, and a suspended tenant the caller
        // does belong to (must be excluded, not returned as suspended).
        var zeta = Tenant.Create($"Zeta {Guid.NewGuid():N}"[..18], $"zeta-{Guid.NewGuid():N}"[..18], now);
        var alpha = Tenant.Create($"Alpha {Guid.NewGuid():N}"[..18], $"alpha-{Guid.NewGuid():N}"[..18], now);
        var stranger = Tenant.Create($"Stranger {Guid.NewGuid():N}"[..20], $"stranger-{Guid.NewGuid():N}"[..18], now);
        var suspended = Tenant.Create($"Suspended {Guid.NewGuid():N}"[..19], $"suspended-{Guid.NewGuid():N}"[..18], now);
        SetStatus(suspended, TenantStatus.Suspended);

        context.Tenants.AddRange(zeta, alpha, stranger, suspended);
        context.TenantMemberships.Add(TenantMembership.CreateOwner(zeta.Id, accountId, now));
        context.TenantMemberships.Add(TenantMembership.CreateMember(alpha.Id, accountId, now));
        context.TenantMemberships.Add(TenantMembership.CreateMember(suspended.Id, accountId, now));
        await context.SaveChangesAsync();

        return new DiscoveryFixture(accountId, zeta.Id, zeta.Name, alpha.Id, alpha.Name, stranger.Id, suspended.Id);
    }

    [Fact]
    public async Task MemberOfTwoActiveTenants_SeesExactlyThoseTwo_OrderedByName()
    {
        var fixture = await ArrangeTenantsAsync();
        using var client = CreateClient();
        Authorize(client, fixture.AccountId);

        var response = await client.GetAsync("/api/auth/me/tenants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenants = document.RootElement.GetProperty("tenants").EnumerateArray().ToList();

        // Exactly the caller's two active memberships: the third tenant and
        // the suspended tenant are both excluded.
        Assert.Equal(2, tenants.Count);

        // Ordered by tenant name, then id: "Alpha ..." sorts before "Zeta ...".
        Assert.Equal(fixture.AlphaName, tenants[0].GetProperty("name").GetString());
        Assert.Equal(fixture.AlphaId, tenants[0].GetProperty("id").GetGuid());
        Assert.Equal("Member", tenants[0].GetProperty("membershipRole").GetString());

        Assert.Equal(fixture.ZetaName, tenants[1].GetProperty("name").GetString());
        Assert.Equal(fixture.ZetaId, tenants[1].GetProperty("id").GetGuid());
        Assert.Equal("Owner", tenants[1].GetProperty("membershipRole").GetString());

        // The discovery DTO stays minimal: id, name, slug, status,
        // membershipRole. No memberCount, no timestamps, no identity fields.
        foreach (var item in tenants)
        {
            Assert.Equal("Active", item.GetProperty("status").GetString());
            Assert.True(item.TryGetProperty("slug", out _));
            Assert.Equal(5, item.EnumerateObject().Count());
            Assert.False(item.TryGetProperty("memberCount", out _));
            Assert.False(item.TryGetProperty("createdAtUtc", out _));
            Assert.False(item.TryGetProperty("email", out _));
        }
    }

    [Fact]
    public async Task NoMemberships_ReturnsEmptyArray()
    {
        await using var context = db.CreateContext();
        var account = Account.CreateUser($"lonely-{Guid.NewGuid():N}@tenantforge.local", "Lonely Account", "hash", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        using var client = CreateClient();
        Authorize(client, account.Id);

        var response = await client.GetAsync("/api/auth/me/tenants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenants = document.RootElement.GetProperty("tenants");
        Assert.Equal(JsonValueKind.Array, tenants.ValueKind);
        Assert.Empty(tenants.EnumerateArray().ToList());
    }

    [Fact]
    public async Task PlatformAdmin_SeesOnlyTheirOwnMemberships_NotAllTenants()
    {
        var fixture = await ArrangeTenantsAsync();

        // Re-issue the same account as a platform admin: the discovery must
        // still be computed from memberships, not from the admin claim.
        await using var context = db.CreateContext();
        var adminAccount = Account.CreateUser($"disc-admin-{Guid.NewGuid():N}@tenantforge.local", "Admin Caller", "hash", DateTimeOffset.UtcNow);
        context.Accounts.Add(adminAccount);
        var adminAccountId = adminAccount.Id;
        context.TenantMemberships.Add(TenantMembership.CreateMember(fixture.AlphaId, adminAccountId, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        using var client = CreateClient();
        Authorize(client, adminAccountId, isPlatformAdmin: true);

        var response = await client.GetAsync("/api/auth/me/tenants");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenants = document.RootElement.GetProperty("tenants").EnumerateArray().ToList();
        var ids = tenants.Select(t => t.GetProperty("id").GetGuid()).ToList();

        // The admin sees only their one membership — no bypass enumerates the
        // caller's other tenants or the suspended/stranger tenants.
        Assert.Single(ids);
        Assert.Equal(fixture.AlphaId, ids[0]);
        Assert.DoesNotContain(fixture.StrangerId, ids);
        Assert.DoesNotContain(fixture.SuspendedId, ids);
    }

    [Fact]
    public async Task DisabledAccount_Returns403_EvenWithValidSignature()
    {
        await using var context = db.CreateContext();
        var account = Account.CreateUser($"disabled-{Guid.NewGuid():N}@tenantforge.local", "Disabled Account", "hash", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Disable after the token would have been minted: a stateless JWT
        // outlives row state, so the endpoint must re-check the account.
        SetStatus(account, AccountStatus.Disabled);
        await context.SaveChangesAsync();

        using var client = CreateClient();
        Authorize(client, account.Id);

        var response = await client.GetAsync("/api/auth/me/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        Assert.False(document.RootElement.TryGetProperty("tenants", out _));
    }

    [Fact]
    public async Task AccountMissingFromDatabase_Returns403()
    {
        // Valid signature and claims, but the sub points at an account that
        // does not exist in iam_accounts.
        using var client = CreateClient();
        Authorize(client, Guid.NewGuid());

        var response = await client.GetAsync("/api/auth/me/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingOrInvalidJwt_Returns401()
    {
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me/tenants")).StatusCode);

        using var malformed = CreateClient();
        malformed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await malformed.GetAsync("/api/auth/me/tenants")).StatusCode);
    }

    [Fact]
    public async Task ProductionWithoutSigningKey_FailsClosedWith401()
    {
        using var productionFactory = new ApiFactory(environment: "Production", seedMode: IamSeedMode.Absent, db);
        using var client = productionFactory.CreateClient();

        // A development-signed token cannot validate in Production: no signing
        // key there, so every protected route answers 401, never data.
        var devToken = TestJwtFactory.Issue(signingKey: ApiFactory.SigningKey);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", devToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me/tenants")).StatusCode);
    }

    // Domain models keep private setters; this small reflection helper lets a
    // test arrange a non-default status without adding a production API for
    // test convenience (same pattern as PlatformAdminSeederTests).
    private static void SetStatus<T>(T entity, Enum status) where T : notnull =>
        typeof(T).GetProperty(nameof(Tenant.Status))!.SetValue(entity, status);

    private sealed record DiscoveryFixture(
        Guid AccountId,
        Guid ZetaId,
        string ZetaName,
        Guid AlphaId,
        string AlphaName,
        Guid StrangerId,
        Guid SuspendedId);
}
