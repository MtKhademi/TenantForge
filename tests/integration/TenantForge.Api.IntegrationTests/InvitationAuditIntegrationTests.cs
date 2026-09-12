using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class InvitationAuditIntegrationTests(IamDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<TestTenant> CreateTenantAsync()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var owner = Account.CreateUser($"owner-{Guid.NewGuid():N}@tenantforge.local", "Sara Rahimi", "hash", now);
        var member = Account.CreateUser($"member-{Guid.NewGuid():N}@tenantforge.local", "Tenant Member", "hash", now);
        var tenant = Tenant.Create($"Acme {Guid.NewGuid():N}"[..18], $"acme-{Guid.NewGuid():N}"[..18], now);
        context.Accounts.AddRange(owner, member);
        context.Tenants.Add(tenant);
        context.TenantMemberships.AddRange(TenantMembership.CreateOwner(tenant.Id, owner.Id, now), TenantMembership.CreateMember(tenant.Id, member.Id, now));
        await context.SaveChangesAsync();
        return new TestTenant(tenant.Id, owner.Id, member.Id);
    }

    private static void Authorize(HttpClient client, Guid accountId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            isPlatformAdmin: false,
            subject: accountId.ToString(),
            email: $"{accountId:N}@tenantforge.local",
            displayName: "Test Account"));
    }

    [Fact]
    public async Task Owner_CreatesPendingInvitation_StoresOnlyHash_AndAuditEvent()
    {
        var tenant = await CreateTenantAsync();
        using var client = CreateClient();
        Authorize(client, tenant.OwnerAccountId);

        var create = await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = " Teammate@Company.COM ", role = "Viewer" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var body = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("teammate@company.com", document.RootElement.GetProperty("email").GetString());
        Assert.Equal("Viewer", document.RootElement.GetProperty("role").GetString());
        Assert.Equal("Pending", document.RootElement.GetProperty("status").GetString());
        Assert.True(DateTimeOffset.Parse(document.RootElement.GetProperty("expiresAtUtc").GetString()!) > DateTimeOffset.UtcNow);

        await using (var context = db.CreateContext())
        {
            var invitation = await context.TenantInvitations.SingleAsync(i => i.TenantId == tenant.TenantId && i.NormalizedEmail == "teammate@company.com");
            Assert.NotEmpty(invitation.TokenHash);
            Assert.DoesNotContain("teammate", invitation.TokenHash, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(64, invitation.TokenHash.Length);
            Assert.Single(await context.AuditEvents.Where(e => e.TenantId == tenant.TenantId && e.Action == "Invitation.Created").ToListAsync());
        }

        var list = await client.GetAsync($"/api/tenants/{tenant.TenantId}/invitations");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("token", await list.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateActiveInvitation_Returns409_ButSameEmailInAnotherTenantAllowed()
    {
        var first = await CreateTenantAsync();
        var second = await CreateTenantAsync();
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        Authorize(firstClient, first.OwnerAccountId);
        Authorize(secondClient, second.OwnerAccountId);
        var request = new { email = "duplicate@company.com", role = "Viewer" };

        Assert.Equal(HttpStatusCode.Created, (await firstClient.PostAsJsonAsync($"/api/tenants/{first.TenantId}/invitations", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await firstClient.PostAsJsonAsync($"/api/tenants/{first.TenantId}/invitations", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await secondClient.PostAsJsonAsync($"/api/tenants/{second.TenantId}/invitations", request)).StatusCode);
    }

    [Fact]
    public async Task InvalidEmailUnknownRoleAndMissingPermissions_AreDeniedSafely()
    {
        var tenant = await CreateTenantAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, tenant.OwnerAccountId);

        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = "not-email", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = "new@company.com", role = "Unknown" })).StatusCode);

        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = "member@company.com", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/audit")).StatusCode);
    }

    [Fact]
    public async Task AuditQuery_IsTenantScoped_FilteredAndNewestFirst()
    {
        var first = await CreateTenantAsync();
        var second = await CreateTenantAsync();
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        Authorize(firstClient, first.OwnerAccountId);
        Authorize(secondClient, second.OwnerAccountId);

        await firstClient.PostAsJsonAsync($"/api/tenants/{first.TenantId}/invitations", new { email = "first@company.com", role = "Viewer" });
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-1).UtcDateTime.ToString("O");
        await firstClient.PostAsJsonAsync($"/api/tenants/{first.TenantId}/invitations", new { email = "latest@company.com", role = "Viewer" });
        await secondClient.PostAsJsonAsync($"/api/tenants/{second.TenantId}/invitations", new { email = "other@company.com", role = "Viewer" });

        var response = await firstClient.GetAsync($"/api/tenants/{first.TenantId}/audit?action=Invitation.Created&fromUtc={Uri.EscapeDataString(cutoff)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var events = document.RootElement.GetProperty("events").EnumerateArray().ToList();
        Assert.True(events.Count >= 1);
        Assert.All(events, evt =>
        {
            Assert.Equal("Invitation.Created", evt.GetProperty("action").GetString());
            Assert.NotEqual("other@company.com", evt.GetProperty("target").GetString());
            Assert.DoesNotContain("token", evt.GetProperty("details").GetString()!, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("latest@company.com", events[0].GetProperty("target").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await firstClient.GetAsync($"/api/tenants/{first.TenantId}/audit?action=Unknown.Action")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await firstClient.GetAsync($"/api/tenants/{first.TenantId}/audit?fromUtc=not-a-date")).StatusCode);
    }

    [Fact]
    public async Task SpecificPermissions_AllowOnlyTheirTenantOperations()
    {
        var tenant = await CreateTenantAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, tenant.OwnerAccountId);

        async Task AssignRoleAsync(string name, string permission)
        {
            var create = await ownerClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new { name, permissionKeys = new[] { permission } });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            using var document = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var roleId = document.RootElement.GetProperty("id").GetGuid();
            await using var context = db.CreateContext();
            var membershipId = await context.TenantMemberships
                .Where(m => m.TenantId == tenant.TenantId && m.AccountId == tenant.MemberAccountId)
                .Select(m => m.Id)
                .SingleAsync();
            Assert.Equal(HttpStatusCode.OK, (await ownerClient.PutAsync($"/api/tenants/{tenant.TenantId}/members/{membershipId}/roles/{roleId}", null)).StatusCode);
        }

        await AssignRoleAsync("Invitation Viewer", "IAM.Invitations.View");
        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = "viewer@company.com", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/audit")).StatusCode);

        await AssignRoleAsync("Invitation Creator", "IAM.Invitations.Create");
        Assert.Equal(HttpStatusCode.Created, (await memberClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = "creator@company.com", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/audit")).StatusCode);

        await AssignRoleAsync("Audit Reader", "IAM.Audit.View");
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/audit")).StatusCode);
    }

    [Fact]
    public async Task RoleChanges_CreateAuditEvents()
    {
        var tenant = await CreateTenantAsync();
        using var client = CreateClient();
        Authorize(client, tenant.OwnerAccountId);

        var create = await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new { name = "Audited Role", permissionKeys = new[] { "IAM.Invitations.View" } });
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var roleId = createDocument.RootElement.GetProperty("id").GetGuid();
        var roles = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenant.TenantId}/roles");
        var memberId = roles.GetProperty("roles").EnumerateArray().Single(role => role.GetProperty("id").GetGuid() == roleId).GetProperty("memberIds").EnumerateArray().ToList();
        Assert.Empty(memberId);

        await client.PutAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles/{roleId}", new { permissionKeys = new[] { "IAM.Invitations.View", "IAM.Invitations.Create" } });
        await using var context = db.CreateContext();
        var membershipId = await context.TenantMemberships.Where(m => m.TenantId == tenant.TenantId && m.AccountId == tenant.MemberAccountId).Select(m => m.Id).SingleAsync();
        await client.PutAsync($"/api/tenants/{tenant.TenantId}/members/{membershipId}/roles/{roleId}", null);
        await client.DeleteAsync($"/api/tenants/{tenant.TenantId}/members/{membershipId}/roles/{roleId}");

        var audit = await client.GetAsync($"/api/tenants/{tenant.TenantId}/audit");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        using var auditDocument = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var actions = auditDocument.RootElement.GetProperty("events").EnumerateArray().Select(evt => evt.GetProperty("action").GetString()).ToList();
        Assert.Contains("Role.Created", actions);
        Assert.Contains("Role.Updated", actions);
        Assert.Contains("Role.Assigned", actions);
        Assert.Contains("Role.Unassigned", actions);
    }

    private sealed record TestTenant(Guid TenantId, Guid OwnerAccountId, Guid MemberAccountId);
}
