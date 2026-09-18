using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B029's tenant-scoped admin surface for per-province shipping
/// rates and coupons — the same B026 authorization pattern (RequireAuthorization
/// + ShopAuthorization's raw-SQL active-membership check) reused without any
/// new permission concept. Locks in the two semantics the Spec calls out:
/// the shipping-rate POST is an idempotent upsert (same id, new cost, never a
/// second row per province), and coupon codes are unique per tenant
/// case-insensitively (the normalized code is what the duplicate check uses),
/// while the same code in a different tenant is perfectly legal.
///
/// Runs on a dedicated database (ShopShippingCouponIsolatedCollection) so its
/// tenants/rates/coupons never inflate the other Shop databases' row counts.
/// </summary>
[Collection(nameof(ShopShippingCouponIsolatedCollection))]
public sealed class ShopShippingCouponAdminIntegrationTests(ShopShippingCouponDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<HttpClient> PlatformAdminClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.Email,
            password = ApiFactory.Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", document.RootElement.GetProperty("accessToken").GetString()!);
        return client;
    }

    private async Task<Tsid> CreateOwnerAccountAsync(string email)
    {
        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Owner", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<string> CreateTenantWithOwnerAsync(HttpClient adminClient, Tsid ownerAccountId, string name)
    {
        var response = await adminClient.PostAsJsonAsync("/api/platform/tenants", new
        {
            name,
            slug = $"shop-{Guid.NewGuid():N}"[..18],
            ownerUserId = TsidId.Format(ownerAccountId)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>A real, non-platform-admin JWT for the given account (a tenant owner).</summary>
    private static void SetMemberToken(HttpClient client, Tsid accountId, string email)
    {
        var token = TestJwtFactory.Issue(
            signingKey: ApiFactory.SigningKey,
            subject: TsidId.Format(accountId),
            email: email,
            displayName: "Shop Owner",
            isPlatformAdmin: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<(string TenantId, HttpClient MemberClient)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var memberClient = CreateClient();
        SetMemberToken(memberClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, memberClient);
    }

    private static async Task<HttpResponseMessage> SetShippingRateAsync(HttpClient client, string tenantId, string? provinceName, decimal cost)
        => await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName, cost });

    /// <summary>
    /// B035: a real, active account with a plain (non-owner) membership in
    /// the given tenant — no role assignment. Seeded through IAM's own
    /// IamDbContext, mirroring the catalog test file's helper.
    /// </summary>
    private async Task<Tsid> CreateMemberAccountAsync(string tenantId, string email)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId);
        if (tenantTsid is null)
        {
            throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        }

        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid.Value, account.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>B035: assigns a tenant role carrying exactly the given permission keys to a membership.</summary>
    private async Task GrantRoleAsync(string tenantId, Tsid accountId, IReadOnlyList<string> permissionKeys)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId);
        if (tenantTsid is null)
        {
            throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        }

        await using var context = db.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var membership = context.TenantMemberships
            .Single(member => member.TenantId == tenantTsid.Value && member.AccountId == accountId);
        var role = TenantRole.Create(tenantTsid.Value, $"Shop Grant {Guid.NewGuid():N}"[..22], permissionKeys, now);
        context.TenantRoles.Add(role);
        context.TenantMemberRoleAssignments.Add(TenantMemberRoleAssignment.Create(membership.Id, role.Id, now));
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> CreateCouponAsync(
        HttpClient client, string tenantId, string? code, string? discountType, decimal discountValue, DateTimeOffset? expiresAtUtc = null)
        => await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons", new { code, discountType, discountValue, expiresAtUtc });

    [Fact]
    public async Task SetShippingRate_IsAnUpsert_SameIdNewCostAndNoSecondRowPerProvince()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        var created = await SetShippingRateAsync(member, tenantId, "Tehran", 50000);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var firstId = createdDoc.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("Tehran", createdDoc.RootElement.GetProperty("provinceName").GetString());
        Assert.Equal(50000m, createdDoc.RootElement.GetProperty("cost").GetDecimal());

        // Same province, new cost: the upsert returns the SAME id with the
        // updated cost — a correction, not a second row.
        var updated = await SetShippingRateAsync(member, tenantId, "Tehran", 75000);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedDoc = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal(firstId, updatedDoc.RootElement.GetProperty("id").GetString());
        Assert.Equal(75000m, updatedDoc.RootElement.GetProperty("cost").GetDecimal());

        // The list confirms exactly one Tehran row at the new cost.
        var list = await member.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        var rates = JsonSerializer.Deserialize<ShippingRateListDto>(listBody, Json)!;
        var tehran = rates.Rates.Where(rate => rate.ProvinceName == "Tehran").ToList();
        Assert.Single(tehran);
        Assert.Equal(firstId, tehran[0].Id);
        Assert.Equal(75000m, tehran[0].Cost);

        // A different province is its own row.
        var isfahan = await SetShippingRateAsync(member, tenantId, "Isfahan", 60000);
        Assert.Equal(HttpStatusCode.OK, isfahan.StatusCode);
        var finalList = (await (await member.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates"))
            .Content.ReadAsStringAsync());
        var finalRates = JsonSerializer.Deserialize<ShippingRateListDto>(finalList, Json)!;
        Assert.Equal(2, finalRates.Rates.Count);
    }

    [Fact]
    public async Task ShippingRateList_IsOrderedByProvinceName()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        // Deliberately created out of alphabetical order.
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Zanjan", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Alborz", 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Mazandaran", 3)).StatusCode);

        var list = await member.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates");
        var rates = (await list.Content.ReadFromJsonAsync<ShippingRateListDto>())!;
        Assert.Equal(new[] { "Alborz", "Mazandaran", "Zanjan" }, rates.Rates.Select(r => r.ProvinceName).ToArray());
    }

    [Fact]
    public async Task SetShippingRate_WithBlankProvince_Returns400_NamingTheField()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        var response = await SetShippingRateAsync(member, tenantId, "   ", 1000);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("provinceName", body, StringComparison.Ordinal);

        // Nothing was persisted.
        var list = (await (await member.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates"))
            .Content.ReadFromJsonAsync<ShippingRateListDto>())!;
        Assert.Empty(list.Rates);
    }

    [Fact]
    public async Task CreateCoupon_ValidPercentageAndFixedAmount_BothPersist()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        var percentage = await CreateCouponAsync(member, tenantId, "WELCOME10", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, percentage.StatusCode);
        using var percentageDoc = JsonDocument.Parse(await percentage.Content.ReadAsStringAsync());
        var percentageId = percentageDoc.RootElement.GetProperty("id").GetString()!;
        Assert.Equal("WELCOME10", percentageDoc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Percentage", percentageDoc.RootElement.GetProperty("discountType").GetString());
        Assert.Equal(10m, percentageDoc.RootElement.GetProperty("discountValue").GetDecimal());
        Assert.True(percentageDoc.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Null(percentageDoc.RootElement.GetProperty("expiresAtUtc").GetString());

        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        var fixedAmount = await CreateCouponAsync(member, tenantId, "FREESHIP", "FixedAmount", 25000, expiry);
        Assert.Equal(HttpStatusCode.Created, fixedAmount.StatusCode);
        var fixedBody = await fixedAmount.Content.ReadAsStringAsync();
        var fixedCoupon = JsonSerializer.Deserialize<CouponDto>(fixedBody, Json)!;
        Assert.Equal("FixedAmount", fixedCoupon.DiscountType);
        Assert.Equal(25000m, fixedCoupon.DiscountValue);
        Assert.NotNull(fixedCoupon.ExpiresAtUtc);

        // The paginated list returns both, ordered by code.
        var list = await member.GetAsync($"/api/tenants/{tenantId}/shop/coupons");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listDto = (await list.Content.ReadFromJsonAsync<CouponListDto>())!;
        Assert.Equal(2, listDto.Coupons.Count);
        Assert.Equal(2, listDto.Pagination.TotalCount);
        Assert.Equal("FREESHIP", listDto.Coupons[0].Code);
        Assert.Equal("WELCOME10", listDto.Coupons[1].Code);
    }

    [Fact]
    public async Task CreateCoupon_WithDuplicateCode_CaseInsensitive_IsRejected_409()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(member, tenantId, "WELCOME10", "Percentage", 10)).StatusCode);

        // Same code, different case: the normalized unique index/logic catches it.
        var duplicate = await CreateCouponAsync(member, tenantId, "welcome10", "Percentage", 15);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var body = await duplicate.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("Duplicate coupon code", problem.RootElement.GetProperty("title").GetString());

        // Only the original coupon exists.
        var list = (await (await member.GetAsync($"/api/tenants/{tenantId}/shop/coupons"))
            .Content.ReadFromJsonAsync<CouponListDto>())!;
        Assert.Single(list.Coupons);
    }

    [Fact]
    public async Task CreateCoupon_WithTheSameCodeInADifferentTenant_IsAllowed()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB) = await NewTenantWithOwnerAsync();

        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(memberA, tenantA, "SUMMER20", "Percentage", 20)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(memberB, tenantB, "SUMMER20", "Percentage", 20)).StatusCode);

        // Each tenant's list shows only its own coupon.
        var listA = (await (await memberA.GetAsync($"/api/tenants/{tenantA}/shop/coupons"))
            .Content.ReadFromJsonAsync<CouponListDto>())!;
        var listB = (await (await memberB.GetAsync($"/api/tenants/{tenantB}/shop/coupons"))
            .Content.ReadFromJsonAsync<CouponListDto>())!;
        Assert.Single(listA.Coupons);
        Assert.Single(listB.Coupons);
    }

    [Fact]
    public async Task CreateCoupon_InvalidInputs_Return400_WithTheRightFieldErrors()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        // Blank code.
        var blankCode = await CreateCouponAsync(member, tenantId, "  ", "Percentage", 10);
        Assert.Equal(HttpStatusCode.BadRequest, blankCode.StatusCode);
        Assert.Contains("code", await blankCode.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Unknown discount type.
        var badType = await CreateCouponAsync(member, tenantId, "X1", "HalfOff", 10);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        Assert.Contains("discountType", await badType.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Percentage above 100.
        var tooBig = await CreateCouponAsync(member, tenantId, "X2", "Percentage", 150);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Contains("discountValue", await tooBig.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Percentage of zero.
        var zero = await CreateCouponAsync(member, tenantId, "X3", "Percentage", 0);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        // FixedAmount of zero / negative.
        var fixedZero = await CreateCouponAsync(member, tenantId, "X4", "FixedAmount", 0);
        Assert.Equal(HttpStatusCode.BadRequest, fixedZero.StatusCode);

        // Nothing was persisted.
        var list = (await (await member.GetAsync($"/api/tenants/{tenantId}/shop/coupons"))
            .Content.ReadFromJsonAsync<CouponListDto>())!;
        Assert.Empty(list.Coupons);
    }

    [Fact]
    public async Task DeactivateCoupon_IsReflectedInTheList_AndAnUnknownCouponIs404()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();

        var created = await CreateCouponAsync(member, tenantId, "GONE", "FixedAmount", 5000);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        var deactivated = await member.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var deactivatedBody = await deactivated.Content.ReadAsStringAsync();
        var deactivatedCoupon = JsonSerializer.Deserialize<CouponDto>(deactivatedBody, Json)!;
        Assert.False(deactivatedCoupon.IsActive);

        // The list immediately reflects the inactive state.
        var list = (await (await member.GetAsync($"/api/tenants/{tenantId}/shop/coupons"))
            .Content.ReadFromJsonAsync<CouponListDto>())!;
        Assert.False(list.Coupons.Single().IsActive);

        // Unknown (well-formed) coupon id: 404, not 400/500.
        var unknown = await member.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{TsidId.Format(TsidId.NewId())}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Malformed coupon id: 404 as well.
        var malformed = await member.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/not-a-coupon/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Fact]
    public async Task ShippingAndCouponRoutes_RequireTenantMembership()
    {
        var (tenantId, _) = await NewTenantWithOwnerAsync();

        // Unauthenticated: 401 on every route.
        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons", new { code = "A", discountType = "Percentage", discountValue = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/tenants/{tenantId}/shop/coupons")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{TsidId.Format(TsidId.NewId())}/deactivate", content: null)).StatusCode);

        // Authenticated, but not a member of this tenant: 403 (the
        // ShopAuthorization raw-SQL membership check), on every route.
        var (otherTenantId, otherMember) = await NewTenantWithOwnerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await otherMember.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherMember.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherMember.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons", new { code = "A", discountType = "Percentage", discountValue = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherMember.GetAsync($"/api/tenants/{tenantId}/shop/coupons")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherMember.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{TsidId.Format(TsidId.NewId())}/deactivate", content: null)).StatusCode);

        // And the member's own tenant works (the happy path from another fact
        // would be circular; prove it here with the same client).
        Assert.Equal(HttpStatusCode.OK, (await otherMember.GetAsync($"/api/tenants/{otherTenantId}/shop/shipping-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherMember.PostAsJsonAsync($"/api/tenants/{otherTenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 1 })).StatusCode);
    }

    // B035: a tenant Owner succeeds on every mutating shipping/coupon endpoint
    // with no role grant at all (Owner bypass) — that is exactly what the
    // Owner-token happy-path tests above already prove end to end.

    [Fact]
    public async Task MemberWithoutAShippingRoleGrant_Gets403_WhenSettingARate()
    {
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        using var client = CreateClient();
        SetMemberToken(client, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        // POST set shipping rate: 403 — the member is active but holds no
        // Shop.Shipping.Manage key (the coupon mutations are gated by the
        // same key).
        Assert.Equal(HttpStatusCode.Forbidden, (await SetShippingRateAsync(client, tenantId, "Tehran", 10000)).StatusCode);

        // Read-only routes stay membership-only (unchanged): the same member
        // can still list rates and coupons.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{tenantId}/shop/shipping-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{tenantId}/shop/coupons")).StatusCode);
    }

    [Fact]
    public async Task MemberWithARoleGrantingShopShippingManage_CanSetARate()
    {
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        await GrantRoleAsync(tenantId, memberAccount, ["Shop.Shipping.Manage"]);
        using var client = CreateClient();
        SetMemberToken(client, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        // The granted key unlocks the shipping-rate upsert (and, by the same
        // key, coupon create/deactivate).
        var set = await SetShippingRateAsync(client, tenantId, "Tehran", 50000);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using var document = JsonDocument.Parse(await set.Content.ReadAsStringAsync());
        Assert.Equal("Tehran", document.RootElement.GetProperty("provinceName").GetString());
        Assert.Equal(50000m, document.RootElement.GetProperty("cost").GetDecimal());

        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(client, tenantId, "WELCOME5", "Percentage", 5)).StatusCode);
    }
}

internal sealed record ShippingRateDto(string Id, string ProvinceName, decimal Cost);

internal sealed record ShippingRateListDto(IReadOnlyList<ShippingRateDto> Rates);

internal sealed record CouponDto(string Id, string Code, string DiscountType, decimal DiscountValue, bool IsActive, DateTimeOffset? ExpiresAtUtc);

internal sealed record CouponListDto(IReadOnlyList<CouponDto> Coupons, ShopPaginationDto Pagination);
