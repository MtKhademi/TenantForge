using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B033's anonymous guest order-lookup endpoint
/// (POST /api/shop/{tenantId}/orders/lookup) and the S30 enumeration-closure
/// decision behind it: a short, human-typed TrackingCode is never sufficient
/// on its own, so the endpoint resolves an order only when the tracking code
/// AND the phone number the order was placed under match together — in one
/// query — and returns one identical, generic not-found body for every
/// order-lookup-miss case (wrong phone, fabricated code, another tenant's
/// code, an unknown well-formed tenant) so a caller can never observe which
/// half was right. A malformed tenant id is a different, earlier rejection:
/// it is dropped at the routing edge with a bare 404 (empty body), never a
/// 400 or a 500, before any query runs.
///
/// The endpoint is read-only and reads the ShopOrderItem snapshot fields, so
/// a later catalog edit (renaming the product) never changes what a past
/// order shows.
///
/// Every fact authors catalog data through B026's authenticated admin API,
/// creates a real order through B031's anonymous API (and, for the
/// status-reflection fact, pays it through B032's), then drives the lookup
/// with a bare client that sends no Authorization header.
///
/// Runs on a dedicated database (ShopOrderLookupIsolatedCollection) so its
/// tenants, orders and payment attempts never inflate the other Shop
/// databases' row counts.
/// </summary>
[Collection(nameof(ShopOrderLookupIsolatedCollection))]
public sealed class ShopOrderLookupIntegrationTests(ShopOrderLookupDbFixture db) : IDisposable
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

    private async Task<(string ProductId, string VariantId)> CreateSingleVariantAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "LOOKUP", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productId = created.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<LookupProductDetailDto>())!;
        return (productId, detail.Variants[0].Id);
    }

    private static Task<HttpResponseMessage> SetShippingRateAsync(HttpClient client, string tenantId, string provinceName, decimal cost)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName, cost });

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
        HttpClient anonymous, string tenantId, string cartId,
        string customerName = "مریم رضایی", string customerPhone = "09121234567")
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName,
            customerPhone,
            shippingProvince = "Tehran",
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode = (string?)null
        });

    private static Task<HttpResponseMessage> PostInitiateAsync(HttpClient anonymous, string tenantId, string orderId)
        => anonymous.PostAsync($"/api/shop/{tenantId}/orders/{orderId}/payments/initiate", content: null);

    private static Task<HttpResponseMessage> PostCallbackAsync(
        HttpClient anonymous, string tenantId, string orderId, string? gatewayReference, bool approved)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders/{orderId}/payments/callback",
            new { gatewayReference, approved });

    private static Task<HttpResponseMessage> PostLookupAsync(
        HttpClient anonymous, string tenantId, string? trackingCode, string? customerPhone)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders/lookup", new { trackingCode, customerPhone });

    /// <summary>
    /// The one, expected not-found body every order-lookup-miss failure case
    /// must match (<see cref="CanonicalProblemBodyAsync"/> strips only the
    /// per-request traceId — the point is that nothing else may differ between
    /// cases).
    /// </summary>
    private static async Task<string> ExpectedNotFoundBodyAsync(HttpClient anonymous, string tenantId)
    {
        var wrongPhone = await PostLookupAsync(anonymous, tenantId, "AAAAAAAAAAAA", "09000000000");
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);
        return await CanonicalProblemBodyAsync(wrongPhone);
    }

    /// <summary>
    /// The endpoint must never let a caller tell which half was wrong, so the
    /// bodies are compared after stripping only the per-request traceId
    /// (ASP.NET Core appends a fresh one to every ProblemDetails response).
    /// The bodies are canonicalized to a string so the comparison is by exact
    /// value, independent of JsonNode object identity or key enumeration.
    /// Everything else — type, title, status, detail — must be identical
    /// across the failure cases.
    /// </summary>
    private static async Task<string> CanonicalProblemBodyAsync(HttpResponseMessage response)
    {
        var body = (JsonObject?)JsonSerializer.Deserialize<JsonNode>(await response.Content.ReadAsStringAsync(), Json)!;
        body.Remove("traceId");
        return body.ToJsonString(Json);
    }

    /// <summary>
    /// Happy-path setup: a tenant with one single-variant product named
    /// "Shirt" at <paramref name="unitPrice"/>, a Tehran shipping rate, and a
    /// real order created through B031's endpoint (PendingPayment, phone
    /// 09121234567). Returns everything the facts need to look the order up
    /// — and the product/category ids, so the snapshot fact can later rename
    /// the product through B026's admin API.
    /// </summary>
    private async Task<Setup> SetupAsync(decimal unitPrice = 890000m, decimal rateCost = 50000m)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var productSlug = $"shirt-{suffix}";
        var (productId, variantId) = await CreateSingleVariantAsync(
            member, tenantId, categoryId, "Shirt", productSlug, stockQuantity: 10, unitPrice);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", rateCost)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantId, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<LookupOrderCreatedDto>())!;
        Assert.Equal("PendingPayment", order.Status);
        return new Setup(tenantId, member, productId, categoryId, productSlug, order);
    }

    /// <summary>
    /// The Shop module's types are internal and not visible to this test
    /// assembly (only IAM has InternalsVisibleTo), so raw SQL is the honest
    /// way to ask the database what a request actually did. Values are
    /// scoped by order id because xunit runs this class's facts in parallel
    /// on the shared collection database (each fact's order has a globally
    /// unique TSID).
    /// </summary>
    private async Task<int> CountAsync(string sql, long value)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("@p", value));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private Task<int> CountOrderItemsForOrderAsync(string orderId)
    {
        var orderLong = TsidId.TryParse(orderId, out var tsid) ? tsid.ToLong() : 0L;
        return CountAsync("select count(*) from shop_order_items where order_id = @p;", orderLong);
    }

    private Task<int> CountPaymentAttemptsForOrderAsync(string orderId)
    {
        var orderLong = TsidId.TryParse(orderId, out var tsid) ? tsid.ToLong() : 0L;
        return CountAsync("select count(*) from shop_payment_attempts where order_id = @p;", orderLong);
    }

    private async Task<string?> GetOrderStatusAsync(string orderId)
    {
        var orderLong = TsidId.TryParse(orderId, out var tsid) ? tsid.ToLong() : 0L;
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "select status from shop_orders where id = @p;";
        command.Parameters.Add(new NpgsqlParameter("@p", orderLong));
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    [Fact]
    public async Task CorrectPair_Returns200_WithCurrentStatus_SnapshotItems_AndTotals()
    {
        var setup = await SetupAsync(unitPrice: 890000m, rateCost: 50000m);
        var order = setup.Order;

        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization); // route is anonymous
        var response = await PostLookupAsync(anonymous, setup.TenantId, order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<LookupResultDto>())!;

        // The current status and the order's own number, not a stale copy.
        Assert.Equal("PendingPayment", body.Status);
        Assert.Equal(order.OrderNumber, body.OrderNumber);

        // The item line comes from the ShopOrderItem snapshot written at
        // order time — the exact label B031 assembled from the variant.
        Assert.Single(body.Items);
        var item = body.Items[0];
        Assert.Equal("Shirt", item.ProductNameSnapshot);
        Assert.Equal("Black / M", item.VariantLabelSnapshot);
        Assert.Equal(890000m, item.UnitPrice);
        Assert.Equal(1, item.Quantity);

        // The stored shipping address, verbatim.
        Assert.Equal("Tehran", body.ShippingProvince);
        Assert.Equal("Tehran", body.ShippingCity);
        Assert.Equal("Valiasr St.", body.ShippingAddressLine);
        Assert.Equal("1234567890", body.ShippingPostalCode);

        // The Spec's worked example: 890000 - 0 + 50000 = 940000.
        Assert.Equal(890000m, body.SubTotal);
        Assert.Equal(50000m, body.ShippingCost);
        Assert.Equal(0m, body.DiscountAmount);
        Assert.Equal(940000m, body.GrandTotal);
    }

    [Fact]
    public async Task WrongPhone_FabricatedCode_AndCrossTenant_AllReturnTheSameGeneric404_Body()
    {
        var setup = await SetupAsync();
        var (tenantB, _) = await NewTenantWithOwnerAsync();

        using var anonymous = CreateClient();
        var expected = await ExpectedNotFoundBodyAsync(anonymous, setup.TenantId);

        // A real tracking code, but the phone number is not the one the order
        // was placed under.
        var wrongPhone = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09000000000");
        Assert.Equal(HttpStatusCode.NotFound, wrongPhone.StatusCode);

        // A tracking code that was never issued.
        var fabricatedCode = await PostLookupAsync(anonymous, setup.TenantId, "ZZZZZZZZZZZZ", "09121234567");
        Assert.Equal(HttpStatusCode.NotFound, fabricatedCode.StatusCode);

        // The real pair, requested under a different tenant: the query is
        // scoped by the route tenantId, so the order is unreachable there.
        var crossTenant = await PostLookupAsync(anonymous, tenantB, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        // No failure may reveal which half (if any) was right: the three
        // not-found bodies are indistinguishable from the expected one and
        // from each other.
        Assert.Equal(expected, await CanonicalProblemBodyAsync(wrongPhone));
        Assert.Equal(expected, await CanonicalProblemBodyAsync(fabricatedCode));
        Assert.Equal(expected, await CanonicalProblemBodyAsync(crossTenant));
    }

    [Fact]
    public async Task MissingTrackingCodeOrPhone_Returns400_NamingTheFields_AndIsNotTheNotFoundShape()
    {
        var setup = await SetupAsync();

        using var anonymous = CreateClient();
        var missingCode = await PostLookupAsync(anonymous, setup.TenantId, null, "09121234567");
        Assert.Equal(HttpStatusCode.BadRequest, missingCode.StatusCode);
        var missingCodeBody = await missingCode.Content.ReadAsStringAsync();
        Assert.Contains("trackingCode", missingCodeBody, StringComparison.Ordinal);
        // A validation problem, never the generic not-found answer.
        Assert.DoesNotContain("No order was found", missingCodeBody, StringComparison.Ordinal);

        var missingPhone = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingPhone.StatusCode);
        var missingPhoneBody = await missingPhone.Content.ReadAsStringAsync();
        Assert.Contains("customerPhone", missingPhoneBody, StringComparison.Ordinal);
        Assert.DoesNotContain("No order was found", missingPhoneBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRenamingTheProduct_TheLookupStillShowsTheOriginalSnapshotName()
    {
        var setup = await SetupAsync(unitPrice: 120000m);

        // The pre-edit lookup shows the name the order was placed under.
        using var anonymous = CreateClient();
        var before = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var beforeBody = (await before.Content.ReadFromJsonAsync<LookupResultDto>())!;
        Assert.Equal("Shirt", beforeBody.Items[0].ProductNameSnapshot);

        // Rename the live product through B026's admin API — a wholesale
        // update that also replaces the variant rows.
        var rename = await setup.Member.PutAsJsonAsync(
            $"/api/tenants/{setup.TenantId}/shop/products/{setup.ProductId}", new
            {
                name = "Premium Renamed Shirt",
                slug = setup.ProductSlug,
                description = (string?)null,
                categoryId = setup.CategoryId,
                basePrice = 120000,
                compareAtPrice = (decimal?)null,
                isActive = true,
                variants = new object[] { new { color = "Black", size = "M", sku = "LOOKUP", stockQuantity = 9, priceOverride = (decimal?)null } },
                sizeGuideColumns = (object?)null,
                sizeGuideRows = (object?)null
            });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // The lookup is snapshot-based: the past order still shows the name
        // it was placed under, even though the live catalog now says
        // otherwise.
        var after = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var afterBody = (await after.Content.ReadFromJsonAsync<LookupResultDto>())!;
        Assert.Equal("Shirt", afterBody.Items[0].ProductNameSnapshot);
        Assert.NotEqual("Premium Renamed Shirt", afterBody.Items[0].ProductNameSnapshot);
    }

    [Fact]
    public async Task AfterPaymentSucceeds_TheLookupShowsThePaidStatus()
    {
        var setup = await SetupAsync();

        using var anonymous = CreateClient();
        var initiate = await PostInitiateAsync(anonymous, setup.TenantId, setup.Order.OrderId);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var initiation = (await initiate.Content.ReadFromJsonAsync<LookupInitiatePaymentDto>())!;
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.Order.OrderId));

        var callback = await PostCallbackAsync(anonymous, setup.TenantId, setup.Order.OrderId, initiation.GatewayReference, approved: true);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.Order.OrderId));

        // The lookup reflects the order's CURRENT status — it reads the live
        // row, it does not cache the state from order time.
        var response = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<LookupResultDto>())!;
        Assert.Equal("Paid", body.Status);
        Assert.Equal(setup.Order.OrderNumber, body.OrderNumber);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task LookupIsReadOnly_ItCreatesNoRowsAndChangesNoRows()
    {
        var setup = await SetupAsync();
        var orderLong = TsidId.TryParse(setup.Order.OrderId, out var tsid) ? tsid.ToLong() : 0L;

        var ordersBefore = 1;
        var itemsBefore = await CountOrderItemsForOrderAsync(setup.Order.OrderId);
        var attemptsBefore = await CountPaymentAttemptsForOrderAsync(setup.Order.OrderId);

        using var anonymous = CreateClient();
        // One success and one failure — neither may touch the database.
        var hit = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
        var miss = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09000000000");
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);

        // Raw SQL is the ground truth: the same single order, the same item
        // lines, no payment attempts, and the order still PendingPayment —
        // the lookup neither advanced nor rewrote it.
        Assert.Equal(ordersBefore, await CountAsync("select count(*) from shop_orders where id = @p;", orderLong));
        Assert.Equal(itemsBefore, await CountOrderItemsForOrderAsync(setup.Order.OrderId));
        Assert.Equal(attemptsBefore, await CountPaymentAttemptsForOrderAsync(setup.Order.OrderId));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.Order.OrderId));
    }

    [Fact]
    public async Task UnknownTenant_SharesTheGeneric404_Body_AndMalformedTenant_IsABare404()
    {
        var setup = await SetupAsync();

        using var anonymous = CreateClient();
        var expected = await ExpectedNotFoundBodyAsync(anonymous, setup.TenantId);

        // A well-formed tenant id that was never created: the query is scoped
        // by the route tenantId, so the order is unreachable there and the
        // body is the one shared not-found — indistinguishable from a wrong
        // phone or a fabricated code.
        var unknownTenant = await PostLookupAsync(anonymous, TsidId.Format(TsidId.NewId()), setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.NotFound, unknownTenant.StatusCode);
        Assert.Equal(expected, await CanonicalProblemBodyAsync(unknownTenant));

        // A malformed tenant id is rejected at the routing edge before any
        // lookup runs: a bare 404 with an empty body (the module convention
        // B027–B032 use for malformed ids) — never a 400, never a 500. It is
        // deliberately NOT the ProblemDetails body, because nothing was
        // looked up to report on.
        var malformed = await PostLookupAsync(anonymous, "not-a-tenant", setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(string.Empty, await malformed.Content.ReadAsStringAsync());

        // The happy path is still intact: the failures above were tenant
        // scoping, not a broken endpoint.
        var ok = await PostLookupAsync(anonymous, setup.TenantId, setup.Order.TrackingCode, "09121234567");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    private sealed record Setup(
        string TenantId, HttpClient Member, string ProductId, string CategoryId, string ProductSlug, LookupOrderCreatedDto Order);
}

internal sealed record LookupVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record LookupProductDetailDto(
    string Id, string CategoryId, string Name, string Slug,
    IReadOnlyList<LookupVariantDto> Variants);

internal sealed record LookupOrderCreatedDto(
    string OrderId, string OrderNumber, string TrackingCode, string Status,
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

internal sealed record LookupInitiatePaymentDto(string GatewayReference, string RedirectUrl);

internal sealed record LookupItemDto(
    string ProductNameSnapshot, string VariantLabelSnapshot, decimal UnitPrice, int Quantity);

internal sealed record LookupResultDto(
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string ShippingProvince,
    string ShippingCity,
    string ShippingAddressLine,
    string ShippingPostalCode,
    decimal SubTotal,
    decimal ShippingCost,
    decimal DiscountAmount,
    decimal GrandTotal,
    IReadOnlyList<LookupItemDto> Items);
