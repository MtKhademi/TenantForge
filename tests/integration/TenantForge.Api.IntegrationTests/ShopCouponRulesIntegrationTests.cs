using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Coupons;
using TenantForge.Modules.Shop.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B041's enforceable coupon rules: minimum subtotal, maximum
/// discount cap, and total redemption limits, evaluated by the single
/// ShopCouponPolicy and consumed atomically at order creation. It locks the
/// honesty rules the Spec calls out: the checkout summary is a pure preview
/// (never increments RedeemedCount), order creation re-validates and increments
/// exactly once under a row lock (a concurrent last-slot race yields one
/// redemption, the loser keeps its cart), and a downstream failure that rolls
/// back the transaction undoes the redemption. Cross-tenant isolation is
/// non-leaking: tenant B's "not found" for tenant A's code is byte-identical to
/// a truly nonexistent code.
///
/// Every fact authors catalog/shipping-rate/coupon data through B026/B029's
/// authenticated admin APIs, builds the cart through B028's anonymous API, and
/// reads persisted state through a real ShopDbContext (now visible to this
/// assembly) or raw SQL.
///
/// Runs on a dedicated database (ShopCouponRulesIsolatedCollection) so its
/// tenants, carts, coupons and orders never inflate the other Shop databases'
/// row counts.
/// </summary>
[Collection(nameof(ShopCouponRulesIsolatedCollection))]
public sealed class ShopCouponRulesIntegrationTests(ShopCouponRulesDbFixture db) : IDisposable
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

    private static async Task<string> CreateCategoryAsync(HttpClient memberClient, string tenantId, string name, string slug)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name,
            slug,
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<CouponVariantDto> CreateSingleVariantAsync(
        HttpClient memberClient, string tenantId, string categoryId,
        string name, string slug, int stockQuantity, decimal basePrice)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "Black", size = "M", sku = "CPN", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<CouponProductDetailDto>())!;
        return detail.Variants[0];
    }

    private static Task<HttpResponseMessage> SetShippingRateAsync(HttpClient client, string tenantId, string provinceName, decimal cost)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName, cost });

    private static Task<HttpResponseMessage> CreateCouponAsync(
        HttpClient client, string tenantId, string code, string discountType, decimal discountValue,
        decimal minimumSubtotal = 0, decimal? maximumDiscountAmount = null,
        int? redemptionLimit = null, DateTimeOffset? expiresAtUtc = null)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons", new
        {
            code,
            discountType,
            discountValue,
            minimumSubtotal,
            maximumDiscountAmount,
            redemptionLimit,
            expiresAtUtc
        });

    private static async Task<string> CreateCartAsync(HttpClient anonymous, string tenantId)
    {
        var response = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("cartId").GetString()!;
    }

    private static Task<HttpResponseMessage> AddItemAsync(
        HttpClient anonymous, string tenantId, string cartId, string variantId, int quantity)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/carts/{cartId}/items", new { productVariantId = variantId, quantity });

    private static Task<HttpResponseMessage> PostCheckoutAsync(
        HttpClient anonymous, string tenantId, string cartId, string shippingProvince, string? couponCode = null)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/checkout/summary", new
        {
            cartId,
            shippingProvince,
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode
        });

    private static Task<HttpResponseMessage> PostOrderAsync(
        HttpClient anonymous, string tenantId, string cartId, string? couponCode = null,
        string? customerName = "مریم رضایی", string? customerPhone = "09121234567")
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName,
            customerPhone,
            shippingProvince = "Tehran",
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode
        });

    private static Task<HttpResponseMessage> UpdateCouponAsync(
        HttpClient client, string tenantId, string couponId, decimal discountValue, decimal minimumSubtotal,
        decimal? maximumDiscountAmount, int? redemptionLimit, DateTimeOffset? expiresAtUtc, bool isActive, int expectedVersion)
        => client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons/{couponId}", new
        {
            discountValue,
            minimumSubtotal,
            maximumDiscountAmount,
            redemptionLimit,
            expiresAtUtc,
            isActive,
            expectedVersion
        });

    private ShopDbContext CreateShopContext() => new(
        new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(db.ConnectionString).Options);

    private async Task<CouponRow> ReadCouponRowAsync(string couponId)
    {
        var couponTsid = TsidId.TryParseNullable(couponId)!;
        await using var shopDb = CreateShopContext();
        var coupon = await shopDb.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponTsid);
        return new CouponRow(coupon.RedeemedCount, coupon.Version, coupon.RedemptionLimit, coupon.IsActive, coupon.DiscountValue, coupon.NormalizedCode);
    }

    private async Task<int> CountOrdersForTenantAsync(string tenantId)
    {
        var tenantLong = TsidId.TryParseNullable(tenantId)!.Value.ToLong();
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shop_orders where tenant_id = @tenantId;";
        command.Parameters.Add(new NpgsqlParameter("@tenantId", tenantLong));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Happy-path setup: a tenant with one single-variant product, a Tehran
    /// shipping rate and a cart holding exactly one unit. Returns the tenant,
    /// the cart, the variant's effective price and the owner's client.
    /// </summary>
    private async Task<(string TenantId, string CartId, decimal UnitPrice, HttpClient Member)> SetupCartAsync(
        decimal unitPrice, decimal rateCost = 50000m, int stockQuantity = 10)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var variant = await CreateSingleVariantAsync(member, tenantId, categoryId, "Shirt", $"shirt-{suffix}", stockQuantity, unitPrice);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", rateCost)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);
        return (tenantId, cartId, variant.EffectivePrice, member);
    }

    // ---- Bullet 1: every ShopCouponPolicy.Evaluate rule boundary returns its
    // ---- exact stable reason code (and a valid coupon returns the capped discount).

    [Fact]
    public async Task Evaluate_ChecksRulesInOrder_ReturnsTheExactStableCode_AndCapsTheDiscount()
    {
        var tenantId = TsidId.NewId();
        var now = DateTimeOffset.UtcNow;

        // Inactive — first failure checked after the caller's not-found lookup.
        var inactive = ShopCoupon.Create(tenantId, "OFF", ShopDiscountType.Percentage, 10, 0, null, null, null);
        inactive.Deactivate();
        Assert.Equal(ShopCouponPolicy.CouponInactive, ShopCouponPolicy.Evaluate(inactive, 100m, now).ErrorCode);

        // Expired — strictly past ExpiresAtUtc.
        var expired = ShopCoupon.Create(tenantId, "OLD", ShopDiscountType.Percentage, 10, 0, null, null, now.AddHours(-1));
        Assert.Equal(ShopCouponPolicy.CouponExpired, ShopCouponPolicy.Evaluate(expired, 100m, now).ErrorCode);

        // Minimum subtotal not met.
        var belowMinimum = ShopCoupon.Create(tenantId, "MIN", ShopDiscountType.Percentage, 10, minimumSubtotal: 500m, null, null, null);
        Assert.Equal(ShopCouponPolicy.CouponMinimumNotMet, ShopCouponPolicy.Evaluate(belowMinimum, 100m, now).ErrorCode);

        // Redemption limit reached — RedeemedCount >= RedemptionLimit.
        var exhausted = ShopCoupon.Create(tenantId, "MAX", ShopDiscountType.Percentage, 10, 0, null, redemptionLimit: 2, null);
        exhausted.RecordRedemption();
        exhausted.RecordRedemption();
        Assert.Equal(ShopCouponPolicy.CouponLimitReached, ShopCouponPolicy.Evaluate(exhausted, 100m, now).ErrorCode);

        // A valid percentage coupon returns the exact rounded discount.
        var valid = ShopCoupon.Create(tenantId, "OK", ShopDiscountType.Percentage, 10, 0, null, null, null);
        var validEval = ShopCouponPolicy.Evaluate(valid, 890000m, now);
        Assert.True(validEval.IsValid);
        Assert.Null(validEval.ErrorCode);
        Assert.Equal(89000m, validEval.DiscountAmount);

        // Bullet 2: a percentage coupon whose raw discount exceeds the maximum
        // is capped at the maximum (raw 10% of 890000 = 89000, cap 50000).
        var capped = ShopCoupon.Create(tenantId, "CAP", ShopDiscountType.Percentage, 10, 0, maximumDiscountAmount: 50000m, null, null);
        Assert.Equal(50000m, ShopCouponPolicy.Evaluate(capped, 890000m, now).DiscountAmount);

        // The cap can never push the applied discount past the subtotal itself.
        var overCap = ShopCoupon.Create(tenantId, "OVR", ShopDiscountType.FixedAmount, 250000m, 0, maximumDiscountAmount: 999999m, null, null);
        Assert.Equal(200000m, ShopCouponPolicy.Evaluate(overCap, 200000m, now).DiscountAmount);
    }

    // ---- Bullet 1 (wire): the stable reason codes surface verbatim in the
    // ---- checkout 400's couponCode field error, and not-found is non-leaking.

    [Fact]
    public async Task Checkout_SurfacesEachStableReasonCode_InTheCouponCodeFieldError()
    {
        var (tenantId, cartId, _, member) = await SetupCartAsync(500000m);

        using var anonymous = CreateClient();

        // coupon_not_found — the code does not exist.
        var missing = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "NOPE");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("(coupon_not_found)", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // coupon_inactive — created then deactivated.
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(member, tenantId, "OFF10", "Percentage", 10)).StatusCode);
        using (var createdDoc = JsonDocument.Parse(await (await CreateCouponAsync(member, tenantId, "OFF11", "Percentage", 10)).Content.ReadAsStringAsync()))
        {
            var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await member.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", content: null)).StatusCode);
        }
        var inactive = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "OFF11");
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("(coupon_inactive)", await inactive.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // coupon_expired — expiry in the past.
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(
            member, tenantId, "OLD10", "Percentage", 10, expiresAtUtc: DateTimeOffset.UtcNow.AddDays(-1))).StatusCode);
        var expired = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "OLD10");
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains("(coupon_expired)", await expired.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // coupon_minimum_not_met — the minimum is above this cart's subtotal.
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(
            member, tenantId, "BIGMIN", "Percentage", 10, minimumSubtotal: 10_000_000m)).StatusCode);
        var minimum = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "BIGMIN");
        Assert.Equal(HttpStatusCode.BadRequest, minimum.StatusCode);
        Assert.Contains("(coupon_minimum_not_met)", await minimum.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---- Bullet 2 (wire): a percentage coupon whose raw discount exceeds the
    // ---- cap is priced at the cap on the checkout summary.

    [Fact]
    public async Task Checkout_PercentageCouponWhoseRawDiscountExceedsMaximum_IsPricedAtTheMaximum()
    {
        var (tenantId, cartId, unitPrice, member) = await SetupCartAsync(890000m);
        // Raw 10% of 890000 is 89000; the cap is 50000.
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(
            member, tenantId, "CAPPED", "Percentage", 10, maximumDiscountAmount: 50000m)).StatusCode);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "CAPPED");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<B041CheckoutSummaryDto>())!;

        Assert.Equal(50000m, summary.DiscountAmount);
        Assert.Equal(unitPrice - 50000m + 50000m, summary.GrandTotal);
    }

    // ---- Bullet 3: the checkout summary is a preview — it never increments
    // ---- RedeemedCount, no matter how many times it is called.

    [Fact]
    public async Task CheckoutSummary_ValidCoupon_DoesNotIncrementRedeemedCount()
    {
        var (tenantId, cartId, _, member) = await SetupCartAsync(500000m);
        var created = await CreateCouponAsync(member, tenantId, "PREVIEW", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var summary = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "PREVIEW");
            Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        }

        // Reload the row straight from the database: still zero.
        var row = await ReadCouponRowAsync(couponId);
        Assert.Equal(0, row.RedeemedCount);
    }

    // ---- Bullet 4: a successful order increments RedeemedCount by exactly one
    // ---- and stores the evaluated discount on the order.

    [Fact]
    public async Task OrderCreation_ValidCoupon_IncrementsRedeemedCountByOne_AndStoresTheEvaluatedDiscount()
    {
        var (tenantId, cartId, unitPrice, member) = await SetupCartAsync(890000m);
        var created = await CreateCouponAsync(member, tenantId, "WELCOME10", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        var response = await PostOrderAsync(anonymous, tenantId, cartId, couponCode: "welcome10");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<B041OrderCreatedDto>())!;

        // The order stored exactly the evaluated discount.
        Assert.Equal(89000m, order.DiscountAmount);

        // And the coupon row advanced by exactly one.
        var row = await ReadCouponRowAsync(couponId);
        Assert.Equal(1, row.RedeemedCount);
        Assert.Equal(unitPrice, order.SubTotal);
    }

    // ---- Bullet 5: a downstream failure after coupon evaluation succeeds
    // ---- rolls the whole transaction back, so the redemption is undone.

    [Fact]
    public async Task OrderCreation_DownstreamFailureAfterCouponEvaluation_RollsBackTheRedemption()
    {
        var (tenantId, cartId, _, member) = await SetupCartAsync(500000m);
        var created = await CreateCouponAsync(member, tenantId, "ROLLBACK", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        // A 150-character customer name exceeds shop_orders.customer_name
        // (varchar(120)). The coupon is evaluated and its RedeemedCount is
        // staged BEFORE this violation is raised inside SaveChangesAsync, so a
        // correct implementation rolls the increment back with the rest of the
        // transaction — the B036 "commit fails, nothing persists" pattern.
        var response = await PostOrderAsync(
            anonymous, tenantId, cartId, couponCode: "ROLLBACK", customerName: new string('x', 150));
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);

        var row = await ReadCouponRowAsync(couponId);
        Assert.Equal(0, row.RedeemedCount);
        Assert.Equal(0, await CountOrdersForTenantAsync(tenantId));
    }

    // ---- Bullet 6: two racing orders for the last available redemption slot —
    // ---- exactly one succeeds with the discount, the other fails its
    // ---- re-validation with coupon_limit_reached, and the loser's cart is left
    // ---- active so it can retry.

    [Fact]
    public async Task OrderCreation_TwoRacingOrdersForTheLastRedemption_ExactlyOneRedeems_AndTheLoserKeepsItsCart()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var variant = await CreateSingleVariantAsync(member, tenantId, categoryId, "Shirt", $"shirt-{suffix}", stockQuantity: 5, basePrice: 500000m);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", 50000)).StatusCode);
        var created = await CreateCouponAsync(member, tenantId, "LAST1", "Percentage", 10, redemptionLimit: 1);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        var cartOne = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartOne, variant.Id, quantity: 1)).StatusCode);
        var cartTwo = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartTwo, variant.Id, quantity: 1)).StatusCode);

        var first = PostOrderAsync(anonymous, tenantId, cartOne, couponCode: "LAST1");
        var second = PostOrderAsync(anonymous, tenantId, cartTwo, couponCode: "LAST1");
        var (firstResponse, secondResponse) = (await first, await second);

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Single(statuses, status => status == HttpStatusCode.Created);
        Assert.Single(statuses, status => status == HttpStatusCode.BadRequest);

        // The loser fails its re-validation with the exact stable code.
        var loserBody = await (firstResponse.StatusCode == HttpStatusCode.BadRequest ? firstResponse : secondResponse).Content.ReadAsStringAsync();
        Assert.Contains("(coupon_limit_reached)", loserBody, StringComparison.Ordinal);

        // Exactly one redemption happened in total.
        Assert.Equal(1, (await ReadCouponRowAsync(couponId)).RedeemedCount);

        // The winner's cart is converted (404); the loser's cart is untouched
        // and still readable as an active cart (200).
        var cartOneState = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartOne}");
        var cartTwoState = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartTwo}");
        Assert.Equal(1, new[] { cartOneState, cartTwoState }.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    // ---- Bullet 7: a stale ExpectedVersion on the admin update is a 409.

    [Fact]
    public async Task UpdateCoupon_StaleExpectedVersion_Returns409_WithTypeStaleVersion()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var created = await CreateCouponAsync(member, tenantId, "STALE", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        var stale = await UpdateCouponAsync(member, tenantId, couponId, 20, 0, null, null, null, true, expectedVersion: 999);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal("stale_version", problem.RootElement.GetProperty("type").GetString());

        // With the correct version the update applies and bumps the version once.
        var ok = await UpdateCouponAsync(member, tenantId, couponId, 20, 0, null, null, null, true, expectedVersion: 0);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = (await ok.Content.ReadFromJsonAsync<B041CouponDto>())!;
        Assert.Equal(1, updated.Version);
        Assert.Equal(20m, updated.DiscountValue);
    }

    // ---- Bullet 8: setting RedemptionLimit below the current RedeemedCount is
    // ---- a validation error and changes nothing.

    [Fact]
    public async Task UpdateCoupon_LimitBelowCurrentRedeemedCount_IsRejected_AndPersistsNoChange()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var v = await CreateSingleVariantAsync(member, tenantId, categoryId, "Shirt", $"shirt-{suffix}", stockQuantity: 5, basePrice: 500000m);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", 50000)).StatusCode);
        var created = await CreateCouponAsync(member, tenantId, "FLOOR", "Percentage", 10, redemptionLimit: 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        // Redeem twice so the floor is a real, in-range-but-too-low limit.
        using var anonymous = CreateClient();
        var cartOne = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartOne, v.Id, quantity: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(anonymous, tenantId, cartOne, couponCode: "FLOOR")).StatusCode);
        var cartTwo = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartTwo, v.Id, quantity: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(anonymous, tenantId, cartTwo, couponCode: "FLOOR")).StatusCode);

        var before = await ReadCouponRowAsync(couponId);
        Assert.Equal(2, before.RedeemedCount);

        // 1 is a valid limit (>= 1) but is below the 2 already redeemed.
        var rejected = await UpdateCouponAsync(member, tenantId, couponId, 10, 0, null, redemptionLimit: 1, null, true, expectedVersion: before.Version);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("redemptionLimit", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Nothing changed: the limit is still 10 and the count is still 2.
        var after = await ReadCouponRowAsync(couponId);
        Assert.Equal(2, after.RedeemedCount);
        Assert.Equal(10, after.RedemptionLimit);
        Assert.Equal(before.Version, after.Version);
    }

    // ---- Bullet 9: tenant B cannot read, update or redeem tenant A's coupon,
    // ---- and any not-found result looks identical to a truly nonexistent code.

    [Fact]
    public async Task Coupon_TenantB_CannotReadUpdateOrRedeemTenantAsCoupon_AndNotFoundIsIdentical()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB) = await NewTenantWithOwnerAsync();
        var created = await CreateCouponAsync(memberA, tenantA, "SECRET", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponAId = createdDoc.RootElement.GetProperty("id").GetString()!;
        var randomId = TsidId.Format(TsidId.NewId());

        // Read: B's list shows none of A's coupons.
        var listB = (await (await memberB.GetAsync($"/api/tenants/{tenantB}/shop/coupons"))
            .Content.ReadFromJsonAsync<B041CouponListDto>())!;
        Assert.Empty(listB.Coupons);

        // Update by A's id: 404, byte-identical to a random unknown id.
        var updateCross = await UpdateCouponAsync(memberB, tenantB, couponAId, 10, 0, null, null, null, true, expectedVersion: 0);
        var updateUnknown = await UpdateCouponAsync(memberB, tenantB, randomId, 10, 0, null, null, null, true, expectedVersion: 0);
        Assert.Equal(HttpStatusCode.NotFound, updateCross.StatusCode);
        Assert.Equal(await updateUnknown.Content.ReadAsStringAsync(), await updateCross.Content.ReadAsStringAsync());

        // Redeem: B's checkout with A's real code is the same non-leaking
        // coupon_not_found as a code that never existed.
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(memberB, tenantB, "Apparel", $"cat-{suffix}");
        var variant = await CreateSingleVariantAsync(memberB, tenantB, categoryId, "Shirt", $"shirt-{suffix}", stockQuantity: 5, basePrice: 500000m);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(memberB, tenantB, "Tehran", 50000)).StatusCode);
        using var anonymous = CreateClient();
        var cartB = await CreateCartAsync(anonymous, tenantB);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantB, cartB, variant.Id, quantity: 1)).StatusCode);

        var redeemA = await PostCheckoutAsync(anonymous, tenantB, cartB, "Tehran", couponCode: "SECRET");
        var redeemGhost = await PostCheckoutAsync(anonymous, tenantB, cartB, "Tehran", couponCode: "GHOSTCODE");
        Assert.Equal(HttpStatusCode.BadRequest, redeemA.StatusCode);
        Assert.Equal(await redeemGhost.Content.ReadAsStringAsync(), await redeemA.Content.ReadAsStringAsync());
        Assert.Contains("(coupon_not_found)", await redeemGhost.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // And A's own coupon is untouched by all of the above.
        Assert.Equal(0, (await ReadCouponRowAsync(couponAId)).RedeemedCount);
    }
}

internal sealed record CouponVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record CouponProductDetailDto(
    string Id, string Name, string Slug, IReadOnlyList<CouponVariantDto> Variants);

internal sealed record B041CouponDto(
    string Id, string Code, string DiscountType, decimal DiscountValue,
    decimal MinimumSubtotal, decimal? MaximumDiscountAmount, int? RedemptionLimit,
    int RedeemedCount, bool IsActive, DateTimeOffset? ExpiresAtUtc, int Version);

internal sealed record B041CouponListDto(IReadOnlyList<B041CouponDto> Coupons, ShopPaginationDto Pagination);

internal sealed record B041CheckoutSummaryDto(
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

internal sealed record B041OrderCreatedDto(
    string OrderId, string OrderNumber, string TrackingCode, string Status,
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

internal sealed record CouponRow(int RedeemedCount, int Version, int? RedemptionLimit, bool IsActive, decimal DiscountValue, string NormalizedCode);
