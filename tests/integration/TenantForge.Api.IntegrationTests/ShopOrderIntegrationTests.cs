using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B031's anonymous order-creation endpoint: it re-runs B030's
/// shipping/coupon validation (never trusting a client-supplied summary) and
/// then, inside one transaction, snapshots the cart into a ShopOrder plus one
/// ShopOrderItem per line, generates a human OrderNumber and a
/// cryptographically-random TrackingCode, and consumes the cart.
///
/// Two honesty rules are locked here:
/// 1. Stock is NOT decremented a second time — B028 already reserved it at
///    add-to-cart time, so a successful order leaves StockQuantity untouched.
/// 2. A double order from the same cart is impossible — cart consumption is
///    part of the transaction, so two concurrent calls yield exactly one 201
///    and exactly one persisted order (the other call finds an empty cart or
///    fails inside the database, never a duplicate order).
///
/// Every fact authors catalog/shipping-rate/coupon data through B026/B029's
/// authenticated admin APIs, builds the cart through B028's anonymous API,
/// then drives the order endpoint with a bare client that sends no
/// Authorization header.
///
/// Runs on a dedicated database (ShopOrderIsolatedCollection) so its tenants,
/// carts, rates, coupons and orders never inflate the other Shop databases'
/// row counts.
/// </summary>
[Collection(nameof(ShopOrderIsolatedCollection))]
public sealed class ShopOrderIntegrationTests(ShopOrderDbFixture db) : IDisposable
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
    private async Task<OrderVariantDto> CreateSingleVariantAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "ORDER", stockQuantity, priceOverride } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<OrderProductDetailDto>())!;
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

    private static Task<HttpResponseMessage> PostOrderAsync(
        HttpClient anonymous, string tenantId, string? cartId,
        string? customerName = "مریم رضایی", string? customerPhone = "09121234567",
        string? shippingProvince = "Tehran", string? couponCode = null)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName,
            customerPhone,
            shippingProvince,
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode
        });

    private static async Task<OrderCartDto> GetCartAsync(HttpClient anonymous, string tenantId, string cartId)
    {
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderCartDto>())!;
    }

    private static async Task<OrderVariantDto> GetVariantAsync(HttpClient anonymous, string tenantId, string productSlug)
    {
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<OrderProductDetailDto>())!;
        return detail.Variants[0];
    }

    /// <summary>
    /// Happy-path setup: a tenant with one single-variant product and a Tehran
    /// shipping rate. Returns the tenant, the product slug, the variant (with
    /// its effective price and live stock) and the tenant owner's
    /// authenticated client.
    /// </summary>
    private async Task<(string TenantId, string ProductSlug, OrderVariantDto Variant, HttpClient Member)> SetupCatalogAsync(
        decimal unitPrice, int stockQuantity = 10, decimal rateCost = 50000m)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var productSlug = $"shirt-{suffix}";
        var variant = await CreateSingleVariantAsync(
            member, tenantId, categoryId, "Shirt", productSlug,
            stockQuantity, unitPrice, null);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", rateCost)).StatusCode);
        return (tenantId, productSlug, variant, member);
    }

    /// <summary>
    /// Counts what a request actually persisted through a raw connection — the
    /// Shop module's types are internal and not visible to this test assembly
    /// (only IAM has InternalsVisibleTo), so raw SQL is the honest way to ask.
    /// Counts are scoped by tenant_id because xunit runs this class's facts in
    /// parallel on the shared collection database: the acceptance criterion is
    /// "exactly one order for this cart" (each fact's cart belongs to a
    /// freshly created tenant), never a global table count.
    /// </summary>
    private async Task<int> CountAsync(string sql, long tenantLong)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("@tenantId", tenantLong));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private Task<int> CountOrdersForTenantAsync(string tenantId)
    {
        var tenantLong = TsidId.TryParse(tenantId, out var tsid) ? tsid.ToLong() : 0L;
        return CountAsync("select count(*) from shop_orders where tenant_id = @tenantId;", tenantLong);
    }

    private Task<int> CountOrderItemsForTenantAsync(string tenantId)
    {
        var tenantLong = TsidId.TryParse(tenantId, out var tsid) ? tsid.ToLong() : 0L;
        return CountAsync(
            "select count(*) from shop_order_items " +
            "join shop_orders on shop_orders.id = shop_order_items.order_id " +
            "where shop_orders.tenant_id = @tenantId;",
            tenantLong);
    }

    [Fact]
    public async Task ValidCart_Returns201_WithSpecWorkedExampleTotals_OrderNumberAndUnguessableTrackingCode()
    {
        var (tenantId, _, variant, member) = await SetupCatalogAsync(890000m);
        Assert.Equal(HttpStatusCode.Created,
            (await CreateCouponAsync(member, tenantId, "WELCOME10", "Percentage", 10)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId, couponCode: "welcome10");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var order = (await response.Content.ReadFromJsonAsync<OrderCreatedDto>())!;

        // The Spec's worked example: 890000 - 89000 + 50000 = 851000.
        Assert.Equal(890000m, order.SubTotal);
        Assert.Equal(89000m, order.DiscountAmount);
        Assert.Equal(50000m, order.ShippingCost);
        Assert.Equal(851000m, order.GrandTotal);

        Assert.Equal("PendingPayment", order.Status);
        Assert.Matches(@"^ORD-\d{6}-\d{4}$", order.OrderNumber);
        // 12 chars, drawn only from the no-0/O/1/I alphabet: unguessable and
        // unambiguous for a guest typing it later (B033).
        Assert.Equal(12, order.TrackingCode.Length);
        Assert.True(order.TrackingCode.All(c => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".Contains(c)),
            $"Tracking code {order.TrackingCode} contains a character outside the restricted alphabet.");
        // A canonical 13-character TSID string — never the backing integer.
        Assert.Equal(13, order.OrderId.Length);
        Assert.Contains($"/api/shop/{tenantId}/orders/{order.OrderId}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulOrder_DoesNotDecrementStockAgain_WhichB028AlreadyReservedAtAddToCartTime()
    {
        var (tenantId, productSlug, variant, _) = await SetupCatalogAsync(500000m, stockQuantity: 10);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        // B028 reserved one unit when the item entered the cart.
        Assert.Equal(9, (await GetVariantAsync(anonymous, tenantId, productSlug)).StockQuantity);

        var response = await PostOrderAsync(anonymous, tenantId, cartId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Order creation consumes the reserved claim; it must NOT decrement
        // StockQuantity a second time (that would oversell the reservation).
        Assert.Equal(9, (await GetVariantAsync(anonymous, tenantId, productSlug)).StockQuantity);
    }

    [Fact]
    public async Task RepeatingTheSameCart_Returns404_BecauseTheCartIsAlreadyConsumed()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(anonymous, tenantId, cartId)).StatusCode);

        // The exact same request immediately after: the cart has no items
        // left, so the answer is the same as if the cart never existed.
        Assert.Equal(HttpStatusCode.NotFound, (await PostOrderAsync(anonymous, tenantId, cartId)).StatusCode);
    }

    [Fact]
    public async Task TwoConcurrentOrdersForTheSameCart_ExactlyOneSucceeds_AndExactlyOneOrderIsPersisted()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var first = PostOrderAsync(anonymous, tenantId, cartId);
        var second = PostOrderAsync(anonymous, tenantId, cartId);
        var (firstResponse, secondResponse) = (await first, await second);

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Single(statuses, status => status == HttpStatusCode.Created);
        // The Spec is explicit about the loser: it finds the cart already
        // consumed by the winner and gets a clean 404 — never a second 201,
        // never an error page.
        Assert.Single(statuses, status => status == HttpStatusCode.NotFound);

        // The database is the source of truth for the "never a duplicate
        // order" guarantee: exactly one order and its one item line for this
        // tenant.
        Assert.Equal(1, await CountOrdersForTenantAsync(tenantId));
        Assert.Equal(1, await CountOrderItemsForTenantAsync(tenantId));
    }

    [Fact]
    public async Task AfterASuccessfulOrder_TheCartHasNoItemsLeft()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 2)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(anonymous, tenantId, cartId)).StatusCode);

        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.SubTotal);
    }

    [Fact]
    public async Task InvalidCoupon_Returns400_NamingTheCouponField_AndCreatesNoOrder()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId, couponCode: "NO-SUCH-CODE");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("couponCode", body, StringComparison.Ordinal);
        Assert.Contains("not valid", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountOrdersForTenantAsync(tenantId));
    }

    [Fact]
    public async Task UnshippableProvince_Returns400_SayingSoPlainly_AndCreatesNoOrder()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m); // only Tehran is configured

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId, shippingProvince: "Khorasan");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("shippingProvince", body, StringComparison.Ordinal);
        Assert.Contains("This tenant does not ship to the selected province.", body, StringComparison.Ordinal);
        Assert.Equal(0, await CountOrdersForTenantAsync(tenantId));
    }

    [Fact]
    public async Task MissingCustomerNameOrPhone_Returns400_NamingTheFields_AndCreatesNoOrder()
    {
        var (tenantId, _, variant, _) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId, customerName: null, customerPhone: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("customerName", body, StringComparison.Ordinal);
        Assert.Contains("customerPhone", body, StringComparison.Ordinal);
        Assert.Equal(0, await CountOrdersForTenantAsync(tenantId));
    }

    [Fact]
    public async Task EmptyOrMissingCart_MissingProvince_AndMalformedIds_AllReturn404_AndCreateNoOrder()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, _) = await NewTenantWithOwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(memberA, tenantA, "Tehran", 50000)).StatusCode);

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

        // A cart with no items: 404, never a zero-subtotal order.
        var emptyCart = await CreateCartAsync(anonymous, tenantA);
        Assert.Equal(HttpStatusCode.NotFound, (await PostOrderAsync(anonymous, tenantA, emptyCart)).StatusCode);
        // A well-formed cart id that does not exist: 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostOrderAsync(anonymous, tenantA, TsidId.Format(TsidId.NewId()))).StatusCode);
        // A malformed cart id: 404, never 400 or 500.
        Assert.Equal(HttpStatusCode.NotFound, (await PostOrderAsync(anonymous, tenantA, "not-a-cart")).StatusCode);
        // A cart that exists but belongs to another tenant: 404 (the existence
        // check is scoped by the route tenantId).
        Assert.Equal(HttpStatusCode.NotFound, (await PostOrderAsync(anonymous, tenantB, cartId)).StatusCode);
        // A malformed tenant id: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await PostOrderAsync(anonymous, "not-a-tenant", cartId)).StatusCode);
        // A missing province is a validation 400, not a 404.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostOrderAsync(anonymous, tenantA, cartId, shippingProvince: null)).StatusCode);

        Assert.Equal(0, await CountOrdersForTenantAsync(tenantA));

        // And the same cart under its own tenant with a shippable province
        // still orders fine: the failures above were validation/scoping, not a
        // broken endpoint.
        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(anonymous, tenantA, cartId)).StatusCode);
    }
}

internal sealed record OrderVariantDto(
    string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record OrderProductDetailDto(
    string Id, string Name, string Slug, IReadOnlyList<OrderVariantDto> Variants);

internal sealed record OrderCreatedDto(
    string OrderId, string OrderNumber, string TrackingCode, string Status,
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

internal sealed record OrderCartDto(string CartId, IReadOnlyList<object> Items, decimal SubTotal);
