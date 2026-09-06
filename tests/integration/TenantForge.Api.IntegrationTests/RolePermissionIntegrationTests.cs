using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(IamApiTestCollection))]
public class RolePermissionIntegrationTests(IamDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<TestTenant> CreateTenantWithOwnerAndMemberAsync()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var owner = Account.CreateUser($"owner-{Guid.NewGuid():N}@tenantforge.local", "Tenant Owner", "hash", now);
        var member = Account.CreateUser($"member-{Guid.NewGuid():N}@tenantforge.local", "Tenant Member", "hash", now);
        var tenant = Tenant.Create($"Acme {Guid.NewGuid():N}"[..18], $"acme-{Guid.NewGuid():N}"[..18], now);
        var ownerMembership = TenantMembership.CreateOwner(tenant.Id, owner.Id, now);
        var memberMembership = TenantMembership.CreateMember(tenant.Id, member.Id, now);
        context.Accounts.AddRange(owner, member);
        context.Tenants.Add(tenant);
        context.TenantMemberships.AddRange(ownerMembership, memberMembership);
        await context.SaveChangesAsync();
        return new TestTenant(tenant.Id, owner.Id, ownerMembership.Id, member.Id, memberMembership.Id);
    }

    private static void Authorize(HttpClient client, Guid accountId, bool isPlatformAdmin = false)
    {
        var token = TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            isPlatformAdmin: isPlatformAdmin,
            subject: accountId.ToString(),
            email: $"{accountId:N}@tenantforge.local",
            displayName: "Test Account");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task Catalog_ReturnsFrozenPermissionKeys()
    {
        using var client = CreateClient();
        Authorize(client, Guid.NewGuid());

        var response = await client.GetAsync("/api/permissions/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = document.RootElement.GetProperty("groups")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("permissions").EnumerateArray())
            .Select(permission => permission.GetProperty("key").GetString())
            .OrderBy(key => key)
            .ToList();

        Assert.Equal([
            "IAM.Dashboard.View",
            "IAM.Tenants.Create",
            "IAM.Tenants.View",
            "IAM.Users.Create",
            "IAM.Users.View"
        ], keys);
    }

    [Fact]
    public async Task Owner_CanCreateReloadAssignAndResolveCustomRole()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var client = CreateClient();
        Authorize(client, tenant.OwnerAccountId);

        var create = await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "User Manager",
            permissionKeys = new[] { "IAM.Users.View", "IAM.Users.Create" }
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createDocument = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var roleId = createDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("custom", createDocument.RootElement.GetProperty("kind").GetString());

        var assign = await client.PutAsync($"/api/tenants/{tenant.TenantId}/members/{tenant.MemberMembershipId}/roles/{roleId}", null);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        var resolved = await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/me/permissions");
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        using var permissionsDocument = JsonDocument.Parse(await resolved.Content.ReadAsStringAsync());
        var permissions = permissionsDocument.RootElement.GetProperty("permissions").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Contains("IAM.Users.View", permissions);
        Assert.Contains("IAM.Users.Create", permissions);

        var reload = await client.GetAsync($"/api/tenants/{tenant.TenantId}/roles");
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        using var reloadDocument = JsonDocument.Parse(await reload.Content.ReadAsStringAsync());
        var reloadedRole = reloadDocument.RootElement.GetProperty("roles").EnumerateArray().Single(role => role.GetProperty("id").GetGuid() == roleId);
        Assert.Contains(tenant.MemberMembershipId, reloadedRole.GetProperty("memberIds").EnumerateArray().Select(item => item.GetGuid()));
    }

    [Fact]
    public async Task SameRoleName_IsTenantScoped()
    {
        var first = await CreateTenantWithOwnerAndMemberAsync();
        var second = await CreateTenantWithOwnerAndMemberAsync();
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        Authorize(firstClient, first.OwnerAccountId);
        Authorize(secondClient, second.OwnerAccountId);

        Assert.Equal(HttpStatusCode.Created, (await firstClient.PostAsJsonAsync($"/api/tenants/{first.TenantId}/roles", new
        {
            name = "User Manager",
            permissionKeys = new[] { "IAM.Users.View" }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await secondClient.PostAsJsonAsync($"/api/tenants/{second.TenantId}/roles", new
        {
            name = "User Manager",
            permissionKeys = new[] { "IAM.Tenants.View" }
        })).StatusCode);
    }

    [Fact]
    public async Task InvalidRoleRequests_Return400_AndDuplicateNameReturns409()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var client = CreateClient();
        Authorize(client, tenant.OwnerAccountId);

        var invalid = await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "",
            permissionKeys = new[] { "IAM.Unknown" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var request = new { name = "User Manager", permissionKeys = new[] { "IAM.Users.View" } };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", request)).StatusCode);
    }

    [Fact]
    public async Task NonMemberAndNonOwner_AreDeniedForRoleManagement()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        var outsider = await CreateTenantWithOwnerAndMemberAsync();
        using var outsiderClient = CreateClient();
        Authorize(outsiderClient, outsider.MemberAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/tenants/{tenant.TenantId}/roles")).StatusCode);

        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "Denied",
            permissionKeys = new[] { "IAM.Users.View" }
        })).StatusCode);
    }

    [Fact]
    public async Task GrantedPermission_AllowsUsersApi_AndUngrantedDirectCallReturns403()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, tenant.OwnerAccountId);
        var create = await ownerClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "User Creator",
            permissionKeys = new[] { "IAM.Users.View", "IAM.Users.Create" }
        });
        using var document = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var roleId = document.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PutAsync($"/api/tenants/{tenant.TenantId}/members/{tenant.MemberMembershipId}/roles/{roleId}", null)).StatusCode);

        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/platform/users?tenantId={tenant.TenantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await memberClient.PostAsJsonAsync($"/api/platform/users?tenantId={tenant.TenantId}", new
        {
            email = $"created-{Guid.NewGuid():N}@tenantforge.local",
            displayName = "Created By Permission",
            password = "permission-password"
        })).StatusCode);

        var otherTenant = await CreateTenantWithOwnerAndMemberAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/platform/users?tenantId={otherTenant.TenantId}")).StatusCode);
    }

    [Fact]
    public async Task LastEffectiveOwnerProtection_Returns409()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var account = Account.CreateUser($"only-owner-{Guid.NewGuid():N}@tenantforge.local", "Only Owner", "hash", now);
        var tenantEntity = Tenant.Create($"Only Owner {Guid.NewGuid():N}"[..24], $"only-{Guid.NewGuid():N}"[..18], now);
        var membership = TenantMembership.CreateMember(tenantEntity.Id, account.Id, now);
        var ownerRole = TenantRole.Create(tenantEntity.Id, "Delegated Owner", ["IAM.Tenants.Create"], now);
        context.Accounts.Add(account);
        context.Tenants.Add(tenantEntity);
        context.TenantMemberships.Add(membership);
        context.TenantRoles.Add(ownerRole);
        context.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(membership.Id, ownerRole.Id, now));
        await context.SaveChangesAsync();

        using var ownerClient = CreateClient();
        Authorize(ownerClient, account.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.DeleteAsync($"/api/tenants/{tenantEntity.Id}/members/{membership.Id}/roles/{ownerRole.Id}")).StatusCode);
    }

    private sealed record TestTenant(Guid TenantId, Guid OwnerAccountId, Guid OwnerMembershipId, Guid MemberAccountId, Guid MemberMembershipId);
}
