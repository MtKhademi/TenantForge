using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

[Collection(nameof(PaginationIsolatedCollection))]
public class PaginationIntegrationTests(PaginationDbFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private static void Authorize(HttpClient client, Guid accountId, bool isPlatformAdmin = false)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            isPlatformAdmin: isPlatformAdmin,
            subject: accountId.ToString(),
            email: $"{accountId:N}@tenantforge.local",
            displayName: "Pagination Account"));
    }

    [Fact]
    public async Task PlatformCollections_PageMetadataAndSecurity_DoNotExposeRowsOrCountsToMembers()
    {
        await using (var context = db.CreateContext())
        {
            var now = DateTimeOffset.UtcNow.AddHours(-2);
            for (var index = 0; index < 60; index++)
            {
                context.Accounts.Add(Account.CreateUser($"page-user-{index:D2}-{Guid.NewGuid():N}@tenantforge.local", $"Page User {index:D2}", "hash", now.AddSeconds(index)));
                context.Tenants.Add(Tenant.Create($"Page Tenant {index:D2}", $"page-tenant-{index:D2}-{Guid.NewGuid():N}"[..30], now.AddSeconds(index)));
            }

            await context.SaveChangesAsync();
        }

        using var adminClient = CreateClient();
        Authorize(adminClient, Guid.NewGuid(), isPlatformAdmin: true);

        var usersPage1 = await GetJsonAsync(adminClient, "/api/platform/users?pageNumber=1&pageSize=20");
        var usersPage2 = await GetJsonAsync(adminClient, "/api/platform/users?pageNumber=2&pageSize=20");
        AssertPagination(usersPage1, 1, 20, minTotalCount: 60, hasPrevious: false, hasNext: true);
        AssertPagination(usersPage2, 2, 20, minTotalCount: 60, hasPrevious: true, hasNext: true);
        AssertDisjoint(usersPage1.GetProperty("users"), usersPage2.GetProperty("users"));

        var tenantsPage2 = await GetJsonAsync(adminClient, "/api/platform/tenants?pageNumber=2&pageSize=25");
        AssertPagination(tenantsPage2, 2, 25, minTotalCount: 60, hasPrevious: true, hasNext: true);
        Assert.Equal(25, tenantsPage2.GetProperty("tenants").GetArrayLength());

        using var memberClient = CreateClient();
        Authorize(memberClient, Guid.NewGuid());
        var denied = await memberClient.GetAsync("/api/platform/users?pageNumber=2&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("pagination", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantScopedCollections_PageAfterScopeAndFilters_KeepStablePayloads()
    {
        var fixture = await ArrangeTenantScopedDataAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, fixture.OwnerAccountId);

        var myTenants = await GetJsonAsync(ownerClient, "/api/auth/me/tenants?pageNumber=2&pageSize=20");
        AssertPagination(myTenants, 2, 20, minTotalCount: 55, hasPrevious: true, hasNext: true);
        Assert.Equal(20, myTenants.GetProperty("tenants").GetArrayLength());
        Assert.DoesNotContain(fixture.SuspendedTenantId, myTenants.GetProperty("tenants").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        var membersPage = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/members?pageNumber=3&pageSize=20");
        Assert.True(membersPage.TryGetProperty("tenant", out _));
        AssertPagination(membersPage, 3, 20, minTotalCount: 56, hasPrevious: true, hasNext: false);
        Assert.True(membersPage.GetProperty("members").GetArrayLength() is > 0 and <= 20);

        var rolesPage = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/roles?pageNumber=2&pageSize=25");
        AssertPagination(rolesPage, 2, 25, minTotalCount: 55, hasPrevious: true, hasNext: true);
        var firstRole = rolesPage.GetProperty("roles").EnumerateArray().First();
        Assert.True(firstRole.GetProperty("permissionKeys").GetArrayLength() >= 1);
        Assert.True(firstRole.GetProperty("memberIds").GetArrayLength() >= 1);

        var invitationsPage = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/invitations?pageNumber=2&pageSize=20");
        AssertPagination(invitationsPage, 2, 20, expectedTotalCount: 55, hasPrevious: true, hasNext: true);
        Assert.Equal(20, invitationsPage.GetProperty("invitations").GetArrayLength());

        var auditPage = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/audit?action=Role.Created&pageNumber=2&pageSize=20");
        AssertPagination(auditPage, 2, 20, expectedTotalCount: 55, hasPrevious: true, hasNext: true);
        Assert.All(auditPage.GetProperty("events").EnumerateArray(), item => Assert.Equal("Role.Created", item.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task EmptyBeyondLastAndInvalidPagination_ReturnExpectedMetadataOrFieldErrors()
    {
        var fixture = await ArrangeTenantScopedDataAsync();
        using var ownerClient = CreateClient();
        Authorize(ownerClient, fixture.OwnerAccountId);

        var beyond = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/members?pageNumber=999&pageSize=10");
        Assert.Empty(beyond.GetProperty("members").EnumerateArray());
        AssertPagination(beyond, 999, 10, minTotalCount: 56, hasPrevious: true, hasNext: false);

        var zeroMatch = await GetJsonAsync(ownerClient, $"/api/tenants/{fixture.PrimaryTenantId}/audit?action=Role.Unassigned&pageNumber=1&pageSize=10");
        Assert.Empty(zeroMatch.GetProperty("events").EnumerateArray());
        AssertPagination(zeroMatch, 1, 10, expectedTotalCount: 0, hasPrevious: false, hasNext: false);

        await AssertValidationErrorAsync(ownerClient, "/api/auth/me/tenants?pageNumber=&pageSize=20", "pageNumber");
        await AssertValidationErrorAsync(ownerClient, "/api/auth/me/tenants?pageNumber=abc&pageSize=20", "pageNumber");
        await AssertValidationErrorAsync(ownerClient, "/api/auth/me/tenants?pageNumber=0&pageSize=20", "pageNumber");
        await AssertValidationErrorAsync(ownerClient, "/api/auth/me/tenants?pageNumber=2147483647&pageSize=100", "pageNumber");
        await AssertValidationErrorAsync(ownerClient, "/api/auth/me/tenants?pageNumber=1&pageSize=101", "pageSize");
    }

    [Fact]
    public async Task TenantPermissionDenialsRemainDeniedOnLaterPages()
    {
        var fixture = await ArrangeTenantScopedDataAsync();
        using var strangerClient = CreateClient();
        Authorize(strangerClient, fixture.StrangerAccountId);

        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/tenants/{fixture.PrimaryTenantId}/members?pageNumber=2&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/tenants/{fixture.PrimaryTenantId}/roles?pageNumber=2&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/tenants/{fixture.PrimaryTenantId}/invitations?pageNumber=2&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/tenants/{fixture.PrimaryTenantId}/audit?pageNumber=2&pageSize=20")).StatusCode);
    }

    private async Task<PaginationFixture> ArrangeTenantScopedDataAsync()
    {
        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow.AddHours(-4);
        var owner = Account.CreateUser($"page-owner-{Guid.NewGuid():N}@tenantforge.local", "Page Owner", "hash", now);
        var stranger = Account.CreateUser($"page-stranger-{Guid.NewGuid():N}@tenantforge.local", "Page Stranger", "hash", now);
        context.Accounts.AddRange(owner, stranger);
        await context.SaveChangesAsync();

        var primary = Tenant.Create($"Primary Page {Guid.NewGuid():N}"[..24], $"primary-page-{Guid.NewGuid():N}"[..30], now);
        var suspended = Tenant.Create($"Suspended Page {Guid.NewGuid():N}"[..26], $"suspended-page-{Guid.NewGuid():N}"[..30], now);
        SetTenantStatus(suspended, TenantStatus.Suspended);
        context.Tenants.AddRange(primary, suspended);
        context.TenantMemberships.Add(TenantMembership.CreateOwner(primary.Id, owner.Id, now));
        context.TenantMemberships.Add(TenantMembership.CreateOwner(suspended.Id, owner.Id, now));

        var assignTarget = Account.CreateUser($"assigned-{Guid.NewGuid():N}@tenantforge.local", "Assigned Member", "hash", now);
        context.Accounts.Add(assignTarget);
        var assignMembership = TenantMembership.CreateMember(primary.Id, assignTarget.Id, now);
        context.TenantMemberships.Add(assignMembership);

        for (var index = 0; index < 55; index++)
        {
            var tenant = Tenant.Create($"Discovery {index:D2}", $"discovery-{index:D2}-{Guid.NewGuid():N}"[..30], now.AddSeconds(index));
            context.Tenants.Add(tenant);
            context.TenantMemberships.Add(TenantMembership.CreateMember(tenant.Id, owner.Id, now.AddSeconds(index)));

            var member = Account.CreateUser($"page-member-{index:D2}-{Guid.NewGuid():N}@tenantforge.local", $"Member {index:D2}", "hash", now.AddSeconds(index));
            context.Accounts.Add(member);
            context.TenantMemberships.Add(TenantMembership.CreateMember(primary.Id, member.Id, now.AddSeconds(index)));

            var role = TenantRole.Create(primary.Id, $"Role {index:D2}", ["IAM.Invitations.View"], now.AddSeconds(index));
            context.TenantRoles.Add(role);
            context.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(assignMembership.Id, role.Id, now.AddSeconds(index)));

            context.TenantInvitations.Add(TenantInvitation.Create(primary.Id, $"invite-{index:D2}@company.com", "Viewer", $"token-{index:D2}-{Guid.NewGuid():N}", now.AddSeconds(index)));
            context.AuditEvents.Add(AuditEvent.Create(primary.Id, owner.Id, owner.DisplayName, owner.Email, "Role.Created", $"Role {index:D2}", "Role created for pagination.", now.AddSeconds(index)));
        }

        var expired = TenantInvitation.Create(primary.Id, "expired@company.com", "Viewer", $"expired-{Guid.NewGuid():N}", now.AddDays(-10));
        context.TenantInvitations.Add(expired);
        var otherTenant = Tenant.Create($"Other Page {Guid.NewGuid():N}"[..22], $"other-page-{Guid.NewGuid():N}"[..30], now);
        context.Tenants.Add(otherTenant);
        context.TenantInvitations.Add(TenantInvitation.Create(otherTenant.Id, "other@company.com", "Viewer", $"other-{Guid.NewGuid():N}", now));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE iam_tenant_invitations SET expires_at_utc = {0} WHERE normalized_email = {1}", DateTimeOffset.UtcNow.AddDays(-1), "expired@company.com");

        return new(primary.Id, owner.Id, stranger.Id, suspended.Id);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertValidationErrorAsync(HttpClient client, string path, string field)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static void AssertPagination(JsonElement root, int pageNumber, int pageSize, int? expectedTotalCount = null, int? minTotalCount = null, bool? hasPrevious = null, bool? hasNext = null)
    {
        var pagination = root.GetProperty("pagination");
        Assert.Equal(pageNumber, pagination.GetProperty("pageNumber").GetInt32());
        Assert.Equal(pageSize, pagination.GetProperty("pageSize").GetInt32());
        var totalCount = pagination.GetProperty("totalCount").GetInt32();
        if (expectedTotalCount is not null) Assert.Equal(expectedTotalCount.Value, totalCount);
        if (minTotalCount is not null) Assert.True(totalCount >= minTotalCount.Value, $"Expected totalCount >= {minTotalCount}, got {totalCount}.");
        Assert.Equal(totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize), pagination.GetProperty("totalPages").GetInt32());
        if (hasPrevious is not null) Assert.Equal(hasPrevious.Value, pagination.GetProperty("hasPreviousPage").GetBoolean());
        if (hasNext is not null) Assert.Equal(hasNext.Value, pagination.GetProperty("hasNextPage").GetBoolean());
    }

    private static void AssertDisjoint(JsonElement first, JsonElement second)
    {
        var firstIds = first.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
        var secondIds = second.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToHashSet();
        Assert.DoesNotContain(secondIds, id => firstIds.Contains(id));
    }

    private static void SetTenantStatus(Tenant tenant, TenantStatus status)
    {
        typeof(Tenant).GetProperty(nameof(Tenant.Status))!.SetValue(tenant, status);
    }

    private sealed record PaginationFixture(Guid PrimaryTenantId, Guid OwnerAccountId, Guid StrangerAccountId, Guid SuspendedTenantId);
}
