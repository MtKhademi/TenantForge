using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class TenantIsolationIntegrationTests(IamDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<Account> CreateUserAsync(string displayName)
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var normalizedName = displayName.ToLowerInvariant().Replace(' ', '-');
        var account = Account.CreateUser(
            $"{normalizedName}-{Guid.NewGuid():N}@tenantforge.local",
            displayName,
            "already-hashed-for-test",
            now);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }

    private async Task<Tenant> CreateTenantWithMembersAsync(string name, params Account[] members)
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create(name, $"tenant-{Guid.NewGuid():N}"[..24], now);
        context.Tenants.Add(tenant);
        foreach (var member in members)
        {
            context.TenantMemberships.Add(TenantMembership.CreateOwner(tenant.Id, member.Id, now));
        }

        await context.SaveChangesAsync();
        return tenant;
    }

    private static void AuthorizeAs(HttpClient client, Account account)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            IssueAccountToken(account));
    }

    private static string IssueAccountToken(Account account)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, account.Email),
                new Claim(JwtRegisteredClaimNames.Name, account.DisplayName),
                new Claim("isPlatformAdmin", account.IsPlatformAdmin ? "true" : "false")
            ]),
            Audience = "TenantForge",
            Issuer = "TenantForge",
            Expires = DateTimeOffset.UtcNow.AddMinutes(30).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public async Task TenantMember_CanReadOnlySelectedTenantMembers()
    {
        var sharedMember = await CreateUserAsync("Shared Owner");
        var acmeOnlyMember = await CreateUserAsync("Acme Owner");
        var globexOnlyMember = await CreateUserAsync("Globex Owner");
        var acme = await CreateTenantWithMembersAsync("Acme", sharedMember, acmeOnlyMember);
        var globex = await CreateTenantWithMembersAsync("Globex", sharedMember, globexOnlyMember);
        using var client = CreateClient();
        AuthorizeAs(client, sharedMember);

        var acmeResponse = await client.GetAsync($"/api/tenants/{acme.Id}/members");
        var globexResponse = await client.GetAsync($"/api/tenants/{globex.Id}/members");

        Assert.Equal(HttpStatusCode.OK, acmeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, globexResponse.StatusCode);

        using var acmeDocument = JsonDocument.Parse(await acmeResponse.Content.ReadAsStringAsync());
        using var globexDocument = JsonDocument.Parse(await globexResponse.Content.ReadAsStringAsync());

        Assert.Equal(acme.Id, acmeDocument.RootElement.GetProperty("tenant").GetProperty("id").GetGuid());
        Assert.Equal(globex.Id, globexDocument.RootElement.GetProperty("tenant").GetProperty("id").GetGuid());

        var acmeEmails = acmeDocument.RootElement.GetProperty("members")
            .EnumerateArray()
            .Select(member => member.GetProperty("email").GetString())
            .ToArray();
        var globexEmails = globexDocument.RootElement.GetProperty("members")
            .EnumerateArray()
            .Select(member => member.GetProperty("email").GetString())
            .ToArray();

        Assert.Contains(sharedMember.Email, acmeEmails);
        Assert.Contains(acmeOnlyMember.Email, acmeEmails);
        Assert.DoesNotContain(globexOnlyMember.Email, acmeEmails);

        Assert.Contains(sharedMember.Email, globexEmails);
        Assert.Contains(globexOnlyMember.Email, globexEmails);
        Assert.DoesNotContain(acmeOnlyMember.Email, globexEmails);
        Assert.NotEqual(acmeEmails.Order().ToArray(), globexEmails.Order().ToArray());
    }

    [Fact]
    public async Task NonMemberRouteTampering_Returns403WithoutLeakingTenantData()
    {
        var acmeMember = await CreateUserAsync("Acme Isolated Owner");
        var globexMember = await CreateUserAsync("Globex Secret Owner");
        await CreateTenantWithMembersAsync("Acme Isolated", acmeMember);
        var globex = await CreateTenantWithMembersAsync("Globex Secret", globexMember);
        using var client = CreateClient();
        AuthorizeAs(client, acmeMember);

        var response = await client.GetAsync($"/api/tenants/{globex.Id}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(globexMember.Email, body);
        Assert.DoesNotContain("Globex Secret", body);
    }

    [Fact]
    public async Task MissingOrInvalidTenantContext_FailsClosed()
    {
        var account = await CreateUserAsync("Context Owner");
        using var client = CreateClient();
        AuthorizeAs(client, account);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tenants/not-a-guid/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/tenants/{Guid.NewGuid()}/members")).StatusCode);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/api/tenants/{Guid.NewGuid()}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformAdminWithoutMembership_IsStillDenied()
    {
        var tenantMember = await CreateUserAsync("Tenant Member For Admin Denial");
        var tenant = await CreateTenantWithMembersAsync("Admin Denied Tenant", tenantMember);
        var platformAdmin = await db.CreateContext().Accounts.SingleAsync(account => account.Email == ApiFactory.Email);
        using var client = CreateClient();
        AuthorizeAs(client, platformAdmin);

        var response = await client.GetAsync($"/api/tenants/{tenant.Id}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
