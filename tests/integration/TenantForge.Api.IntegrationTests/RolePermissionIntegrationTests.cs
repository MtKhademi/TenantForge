using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(RolePermissionIsolatedCollection))]
public class RolePermissionIntegrationTests(RolePermissionDbFixture db) : IDisposable
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
    public async Task Catalog_ReturnsS12PermissionKeysOnly()
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
            "IAM.Audit.View",
            "IAM.Invitations.Create",
            "IAM.Invitations.View",
            "IAM.Roles.Manage"
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
            name = "Invitation Viewer",
            permissionKeys = new[] { "IAM.Invitations.View" }
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
        Assert.Equal(["IAM.Invitations.View"], permissions);

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
            name = "Invitation Viewer",
            permissionKeys = new[] { "IAM.Invitations.View" }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await secondClient.PostAsJsonAsync($"/api/tenants/{second.TenantId}/roles", new
        {
            name = "Invitation Viewer",
            permissionKeys = new[] { "IAM.Audit.View" }
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
        using (var doc = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("permissionKeys", out _));
        }

        var obsolete = await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "Old Permission",
            permissionKeys = new[] { "IAM.Users.View" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, obsolete.StatusCode);
        using (var doc = JsonDocument.Parse(await obsolete.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("permissionKeys", out _));
        }

        var request = new { name = "Invitation Viewer", permissionKeys = new[] { "IAM.Invitations.View" } };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", request)).StatusCode);
    }

    [Fact]
    public async Task NonMemberAndNonManager_AreDeniedForRoleManagement()
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
            permissionKeys = new[] { "IAM.Invitations.View" }
        })).StatusCode);
    }

    [Fact]
    public async Task RolesManageMember_CanManageRoles_WithoutPlatformAccess()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, tenant.OwnerAccountId);

        var createManager = await ownerClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "Role Manager",
            permissionKeys = new[] { "IAM.Roles.Manage" }
        });
        Assert.Equal(HttpStatusCode.Created, createManager.StatusCode);
        using var managerDoc = JsonDocument.Parse(await createManager.Content.ReadAsStringAsync());
        var managerRoleId = managerDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PutAsync($"/api/tenants/{tenant.TenantId}/members/{tenant.MemberMembershipId}/roles/{managerRoleId}", null)).StatusCode);

        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);
        var createManaged = await memberClient.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/roles", new
        {
            name = "Audit Reader",
            permissionKeys = new[] { "IAM.Audit.View" }
        });
        Assert.Equal(HttpStatusCode.Created, createManaged.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/platform/users?tenantId={tenant.TenantId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/audit")).StatusCode);
    }

    [Fact]
    public async Task OwnerAndOrdinaryMemberResolvedPermissionsMatchServerDecisions()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, tenant.OwnerAccountId);
        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);

        var ownerPermissions = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenant.TenantId}/me/permissions");
        Assert.Equal([
            "IAM.Audit.View",
            "IAM.Invitations.Create",
            "IAM.Invitations.View",
            "IAM.Roles.Manage"
        ], ownerPermissions.GetProperty("permissions").EnumerateArray().Select(item => item.GetString()).OrderBy(item => item).ToList());

        var memberPermissions = await memberClient.GetFromJsonAsync<JsonElement>($"/api/tenants/{tenant.TenantId}/me/permissions");
        Assert.Empty(memberPermissions.GetProperty("permissions").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/invitations")).StatusCode);
    }

    [Fact]
    public async Task TenantAuthorizationRejectsRemovedMembershipSuspendedTenantAndDisabledAccount()
    {
        var tenant = await CreateTenantWithOwnerAndMemberAsync();
        using var memberClient = CreateClient();
        Authorize(memberClient, tenant.MemberAccountId);

        await using (var context = db.CreateContext())
        {
            var membership = await context.TenantMemberships.SingleAsync(m => m.Id == tenant.MemberMembershipId);
            context.TenantMemberships.Remove(membership);
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync($"/api/tenants/{tenant.TenantId}/roles")).StatusCode);

        var suspended = await CreateTenantWithOwnerAndMemberAsync();
        using var suspendedClient = CreateClient();
        Authorize(suspendedClient, suspended.OwnerAccountId);
        await using (var context = db.CreateContext())
        {
            var tenantEntity = await context.Tenants.SingleAsync(t => t.Id == suspended.TenantId);
            SetStatus(tenantEntity, TenantStatus.Suspended);
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await suspendedClient.GetAsync($"/api/tenants/{suspended.TenantId}/roles")).StatusCode);

        var disabled = await CreateTenantWithOwnerAndMemberAsync();
        using var disabledClient = CreateClient();
        Authorize(disabledClient, disabled.OwnerAccountId);
        await using (var context = db.CreateContext())
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == disabled.OwnerAccountId);
            SetStatus(account, AccountStatus.Disabled);
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await disabledClient.GetAsync($"/api/tenants/{disabled.TenantId}/roles")).StatusCode);
    }

    [Fact]
    public async Task LastAdministratorProtection_EvaluatesDistinctFinalActiveAccounts()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var admin = Account.CreateUser($"admin-{Guid.NewGuid():N}@tenantforge.local", "Delegated Admin", "hash", now);
        var member = Account.CreateUser($"member-{Guid.NewGuid():N}@tenantforge.local", "Plain Member", "hash", now);
        var tenantEntity = Tenant.Create($"Admin {Guid.NewGuid():N}"[..18], $"admin-{Guid.NewGuid():N}"[..18], now);
        var adminMembership = TenantMembership.CreateMember(tenantEntity.Id, admin.Id, now);
        var memberMembership = TenantMembership.CreateMember(tenantEntity.Id, member.Id, now);
        var adminRole = TenantRole.Create(tenantEntity.Id, "Delegated Admin", ["IAM.Roles.Manage"], now);
        var duplicateAdminRole = TenantRole.Create(tenantEntity.Id, "Duplicate Admin", ["IAM.Roles.Manage"], now);
        context.Accounts.AddRange(admin, member);
        context.Tenants.Add(tenantEntity);
        context.TenantMemberships.AddRange(adminMembership, memberMembership);
        context.TenantRoles.AddRange(adminRole, duplicateAdminRole);
        context.TenantMemberRoleAssignments.AddRange(
            TenantMemberRoleAssignment.Create(adminMembership.Id, adminRole.Id, now),
            TenantMemberRoleAssignment.Create(adminMembership.Id, duplicateAdminRole.Id, now));
        await context.SaveChangesAsync();

        using var adminClient = CreateClient();
        Authorize(adminClient, admin.Id);

        // Two admin role assignments on the same account still count as one
        // distinct administrator account. Removing one duplicate assignment is
        // safe because the same account still holds the second manager role.
        Assert.Equal(HttpStatusCode.OK, (await adminClient.DeleteAsync($"/api/tenants/{tenantEntity.Id}/members/{adminMembership.Id}/roles/{adminRole.Id}")).StatusCode);

        // Removing or weakening the remaining grant would leave zero distinct
        // administrator accounts and must be rejected.
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.DeleteAsync($"/api/tenants/{tenantEntity.Id}/members/{adminMembership.Id}/roles/{duplicateAdminRole.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsJsonAsync($"/api/tenants/{tenantEntity.Id}/roles/{duplicateAdminRole.Id}", new { permissionKeys = new[] { "IAM.Audit.View" } })).StatusCode);

        var secondAdminRole = TenantRole.Create(tenantEntity.Id, "Second Admin", ["IAM.Roles.Manage"], now);
        context.TenantRoles.Add(secondAdminRole);
        context.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(memberMembership.Id, secondAdminRole.Id, now));
        await context.SaveChangesAsync();

        Assert.Equal(HttpStatusCode.OK, (await adminClient.DeleteAsync($"/api/tenants/{tenantEntity.Id}/members/{adminMembership.Id}/roles/{duplicateAdminRole.Id}")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentLastAdministratorDemotions_DoNotBothSucceed()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var admin = Account.CreateUser($"concurrent-admin-{Guid.NewGuid():N}@tenantforge.local", "Concurrent Admin", "hash", now);
        var tenantEntity = Tenant.Create($"Concurrent {Guid.NewGuid():N}"[..24], $"concurrent-{Guid.NewGuid():N}"[..22], now);
        var membership = TenantMembership.CreateMember(tenantEntity.Id, admin.Id, now);
        var firstRole = TenantRole.Create(tenantEntity.Id, "First Manager", ["IAM.Roles.Manage"], now);
        var secondRole = TenantRole.Create(tenantEntity.Id, "Second Manager", ["IAM.Roles.Manage"], now);
        context.Accounts.Add(admin);
        context.Tenants.Add(tenantEntity);
        context.TenantMemberships.Add(membership);
        context.TenantRoles.AddRange(firstRole, secondRole);
        context.TenantMemberRoleAssignments.AddRange(
            TenantMemberRoleAssignment.Create(membership.Id, firstRole.Id, now),
            TenantMemberRoleAssignment.Create(membership.Id, secondRole.Id, now));
        await context.SaveChangesAsync();

        using var adminClient = CreateClient();
        Authorize(adminClient, admin.Id);

        var removeA = adminClient.DeleteAsync($"/api/tenants/{tenantEntity.Id}/members/{membership.Id}/roles/{firstRole.Id}");
        var updateB = adminClient.PutAsJsonAsync($"/api/tenants/{tenantEntity.Id}/roles/{secondRole.Id}", new { permissionKeys = new[] { "IAM.Audit.View" } });
        var results = await Task.WhenAll(removeA, updateB);

        Assert.Contains(results, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(results, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    private static void SetStatus<T>(T entity, Enum status) where T : notnull =>
        typeof(T).GetProperty("Status")!.SetValue(entity, status);

    private sealed record TestTenant(Guid TenantId, Guid OwnerAccountId, Guid OwnerMembershipId, Guid MemberAccountId, Guid MemberMembershipId);
}
