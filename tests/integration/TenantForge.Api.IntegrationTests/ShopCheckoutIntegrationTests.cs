using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B030's anonymous, read/compute-only checkout summary: the endpoint
/// prices a cart against the tenant's configured per-province shipping rate and
/// an optional coupon, and persists nothing. It locks the two honesty rules the
/// source slice calls out: a province with no configured rate returns a clear
/// 400 ("does not ship to the selected province") — never a 200 with a zero
/// shipping cost — and an empty or nonexistent cart returns 404 — never a
/// zero-subtotal summary that looks like a valid empty order.
///
/// Every fact authors catalog/shipping-rate/coupon data through B026/B029's
/// authenticated admin APIs and builds the cart through B028's anonymous API,
/// then drives the checkout endpoint with a bare client that sends no
/// Authorization header — proving the route is genuinely anonymous while
/// tenant isolation is enforced server-side by the {tenantId} route segment on
/// every query.
///
/// Runs on a dedicated database (ShopCheckoutIsolatedCollection) so its
/// tenants, carts, rates and coupons never inflate the other Shop databases'
/// row counts.
/// </summary>
[Collection(nameof(ShopCheckoutIsolatedCollection))]
public sealed class ShopCheckoutIntegrationTests(ShopCheckoutDbFixture db) : IDisposable
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

    /// <summary>
    /// Authors a single-variant product through B026's admin API and returns the
    /// variant row read back through B027's anonymous product-detail endpoint
    /// (id + live stock + effective price) — the same way a shopper would see it.
    /// </summary>
    private async Task<CheckoutVariantDto> CreateSingleVariantAsync(
        HttpClient memberClient, string tenantId, string categoryId,
        string name, string slug, int stockQuantity, decimal basePrice, decimal? priceOverride)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "Black", size = "M", sku = "CHECKOUT", stockQuantity, priceOverride } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<CheckoutProductDetailDto>())!;
        return detail.Variants[0];
    }

    private static Task<HttpResponseMessage> SetShippingRateAsync(HttpClient client, string tenantId, string provinceName, decimal cost)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName, cost });

    private static Task<HttpResponseMessage> CreateCouponAsync(
        HttpClient client, string tenantId, string code, string discountType, decimal discountValue, DateTimeOffset? expiresAtUtc = null)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/coupons", new { code, discountType, discountValue, expiresAtUtc });

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
        HttpClient anonymous, string tenantId, string? cartId, string? shippingProvince, string? couponCode = null)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/checkout/summary", new
        {
            cartId,
            shippingProvince,
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode
        });

    /// <summary>
    /// Happy-path setup: a tenant with one single-variant product, a Tehran
    /// shipping rate, and a cart holding exactly one unit of the variant.
    /// Returns the tenant, the cart, the variant's effective (snapshot) price
    /// and the tenant owner's authenticated client (to author coupons).
    /// </summary>
    private async Task<(string TenantId, string CartId, decimal UnitPrice, HttpClient Member)> SetupCartWithRateAsync(
        decimal unitPrice, decimal rateCost = 50000m)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var variant = await CreateSingleVariantAsync(
            member, tenantId, categoryId, "Shirt", $"shirt-{suffix}",
            stockQuantity: 10, basePrice: unitPrice, priceOverride: null);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", rateCost)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);
        return (tenantId, cartId, variant.EffectivePrice, member);
    }

    [Fact]
    public async Task ValidCart_ShippableProvince_NoCoupon_ReturnsPricedSummaryWithZeroDiscount()
    {
        var (tenantId, cartId, unitPrice, _) = await SetupCartWithRateAsync(500000m);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<CheckoutSummaryDto>())!;

        Assert.Equal(unitPrice, summary.SubTotal);
        Assert.Equal(0m, summary.DiscountAmount);
        Assert.Equal(50000m, summary.ShippingCost);
        Assert.Equal(unitPrice + 50000m, summary.GrandTotal);
    }

    [Fact]
    public async Task ValidCart_WithPercentageCoupon_CutsGrandTotalByThePercentage_OfSubTotal()
    {
        var (tenantId, cartId, unitPrice, member) = await SetupCartWithRateAsync(890000m);
        Assert.Equal(HttpStatusCode.Created,
            (await CreateCouponAsync(member, tenantId, "WELCOME10", "Percentage", 10)).StatusCode);

        using var anonymous = CreateClient();
        // The code was stored uppercase; sending it lowercase must still work —
        // the lookup runs on the normalized code.
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "welcome10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<CheckoutSummaryDto>())!;

        Assert.Equal(unitPrice, summary.SubTotal);
        Assert.Equal(89000m, summary.DiscountAmount); // round(890000 * 10 / 100, 2)
        Assert.Equal(50000m, summary.ShippingCost);
        Assert.Equal(unitPrice - 89000m + 50000m, summary.GrandTotal); // 851000, the Spec's worked example
    }

    [Fact]
    public async Task ValidCart_WithFixedAmountCouponAboveSubTotal_DiscountIsCappedAtTheSubTotal()
    {
        var (tenantId, cartId, unitPrice, member) = await SetupCartWithRateAsync(200000m, rateCost: 10000m);
        // 250000 > the 200000 subtotal: the coupon can never push the goods
        // total below zero.
        Assert.Equal(HttpStatusCode.Created,
            (await CreateCouponAsync(member, tenantId, "BIGFIVE", "FixedAmount", 250000)).StatusCode);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "BIGFIVE");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<CheckoutSummaryDto>())!;

        Assert.Equal(200000m, summary.SubTotal);
        Assert.Equal(200000m, summary.DiscountAmount); // min(250000, 200000)
        Assert.Equal(10000m, summary.ShippingCost);
        Assert.Equal(10000m, summary.GrandTotal);      // only the shipping remains
    }

    [Fact]
    public async Task ExpiredCoupon_Returns400_NamingTheCouponField_NeverA200ThatIgnoresIt()
    {
        var (tenantId, cartId, _, member) = await SetupCartWithRateAsync(500000m);
        Assert.Equal(HttpStatusCode.Created, (await CreateCouponAsync(
            member, tenantId, "OLD10", "Percentage", 10, DateTimeOffset.UtcNow.AddDays(-1))).StatusCode);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "OLD10");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("couponCode", body, StringComparison.Ordinal);
        Assert.Contains("not valid", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivatedCoupon_Returns400_NamingTheCouponField()
    {
        var (tenantId, cartId, _, member) = await SetupCartWithRateAsync(500000m);
        var created = await CreateCouponAsync(member, tenantId, "GONE10", "Percentage", 10);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var couponId = createdDoc.RootElement.GetProperty("id").GetString()!;

        var deactivated = await member.PatchAsync($"/api/tenants/{tenantId}/shop/coupons/{couponId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran", couponCode: "GONE10");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("couponCode", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnshippableProvince_Returns400_SayingSoPlainly_NeverA200WithZeroShipping()
    {
        var (tenantId, cartId, _, _) = await SetupCartWithRateAsync(500000m); // only Tehran is configured

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Khorasan");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("shippingProvince", body, StringComparison.Ordinal);
        Assert.Contains("This tenant does not ship to the selected province.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingProvince_Returns400_NamingTheField()
    {
        var (tenantId, cartId, _, _) = await SetupCartWithRateAsync(500000m);

        using var anonymous = CreateClient();
        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, shippingProvince: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("shippingProvince", body, StringComparison.Ordinal);
        Assert.Contains("required", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyCart_Returns404_NeverAZeroSubtotalSummary()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", 50000)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId); // no items added

        var response = await PostCheckoutAsync(anonymous, tenantId, cartId, "Tehran");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonexistentMalformedOrOtherTenantCart_AllReturn404()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, _) = await NewTenantWithOwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(memberA, tenantA, "Tehran", 50000)).StatusCode);

        // Give the cart one line so the happy-path comparison is a real 200
        // (an empty cart is 404 by design, which would muddle the isolation
        // assertion below).
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(memberA, tenantA, "Apparel", $"cat-{suffix}");
        var variant = await CreateSingleVariantAsync(
            memberA, tenantA, categoryId, "Shirt", $"shirt-{suffix}",
            stockQuantity: 5, basePrice: 100000, priceOverride: null);

        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization); // route is anonymous
        var cartId = await CreateCartAsync(anonymous, tenantA);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantA, cartId, variant.Id, quantity: 1)).StatusCode);

        // A well-formed cart id that does not exist: 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostCheckoutAsync(anonymous, tenantA, TsidId.Format(TsidId.NewId()), "Tehran")).StatusCode);
        // A malformed cart id: 404, never 400 or 500.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostCheckoutAsync(anonymous, tenantA, "not-a-cart", "Tehran")).StatusCode);
        // A cart that exists but belongs to another tenant: 404 (the existence
        // check is scoped by the route tenantId).
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostCheckoutAsync(anonymous, tenantB, cartId, "Tehran")).StatusCode);
        // A malformed tenant id: 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostCheckoutAsync(anonymous, "not-a-tenant", cartId, "Tehran")).StatusCode);
        // And the same cart under its own tenant still prices fine: the 404s
        // above were tenant scoping, not a broken endpoint.
        Assert.Equal(HttpStatusCode.OK,
            (await PostCheckoutAsync(anonymous, tenantA, cartId, "Tehran")).StatusCode);
    }
}

internal sealed record CheckoutVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record CheckoutProductDetailDto(
    string Id, string Name, string Slug, IReadOnlyList<CheckoutVariantDto> Variants);

internal sealed record CheckoutSummaryDto(
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);
