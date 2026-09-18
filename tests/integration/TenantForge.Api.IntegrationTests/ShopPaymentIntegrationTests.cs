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
/// Protects B032's two anonymous sandbox-payment endpoints and the
/// IShopPaymentGateway seam behind them:
///
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/callback
///
/// "Initiating" mints a ShopPaymentAttempt (Status = Initiated) and a
/// redirect target to the in-app fake bank page; the callback verifies that
/// attempt and transitions the order to Paid (attempt Succeeded) or leaves it
/// PendingPayment (attempt Failed). SandboxPaymentGateway makes no outbound
/// HTTP call — that is the deliberate S29 scope boundary.
///
/// Two honesty rules are locked here:
/// 1. The order only ever moves PendingPayment → Paid (MarkPaid) or back to
///    PendingPayment (MarkPaymentFailed); Cancelled/Fulfilled are never set
///    from these endpoints.
/// 2. An attempt resolves exactly once (TryResolve): an unknown gateway
///    reference and a repeated callback are both rejected, never silently
///    accepted as a second resolution.
///
/// Every fact authors catalog data through B026's authenticated admin API and
/// creates a real order through B031's anonymous API, then drives the payment
/// endpoints with a bare client that sends no Authorization header.
///
/// Runs on a dedicated database (ShopPaymentIsolatedCollection) so its
/// tenants, orders and payment attempts never inflate the other Shop
/// databases' row counts.
/// </summary>
[Collection(nameof(ShopPaymentIsolatedCollection))]
public sealed class ShopPaymentIntegrationTests(ShopPaymentDbFixture db) : IDisposable
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

    private async Task<PaymentVariantDto> CreateSingleVariantAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "PAY", stockQuantity, priceOverride } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<PaymentProductDetailDto>())!;
        return detail.Variants[0];
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

    /// <summary>
    /// Happy-path setup: a tenant with one single-variant product, a Tehran
    /// shipping rate, and a real PendingPayment order created through B031's
    /// endpoint. Returns the tenant id, the new order id and the owner's
    /// authenticated client.
    /// </summary>
    private async Task<(string TenantId, string OrderId, HttpClient Member)> SetupPendingOrderAsync()
    {
        var (tenantId, _, variant, member) = await SetupCatalogAsync(500000m);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK,
            (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<PaymentOrderCreatedDto>())!;
        Assert.Equal("PendingPayment", order.Status);
        return (tenantId, order.OrderId, member);
    }

    private async Task<(string TenantId, string ProductSlug, PaymentVariantDto Variant, HttpClient Member)> SetupCatalogAsync(
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
    /// The Shop module's types are internal and not visible to this test
    /// assembly (only IAM has InternalsVisibleTo), so raw SQL is the honest
    /// way to ask the database what a request actually persisted. Counts are
    /// scoped by order id because xunit runs this class's facts in parallel on
    /// the shared collection database (each fact's order has a globally unique
    /// TSID).
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

    private Task<int> CountAttemptsForOrderAsync(string orderId)
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

    private async Task<string?> GetAttemptStatusAsync(string gatewayReference)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "select status from shop_payment_attempts where gateway_reference = @ref;";
        command.Parameters.Add(new NpgsqlParameter("@ref", gatewayReference));
        var result = await command.ExecuteScalarAsync();
        return result as string;
    }

    [Fact]
    public async Task Initiate_ValidPendingPaymentOrder_Returns200_WithGatewayReferenceAndRedirectTarget_AndPersistsInitiatedAttempt()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization); // route is anonymous
        var response = await PostInitiateAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var initiation = (await response.Content.ReadFromJsonAsync<InitiatePaymentDto>())!;
        // 16 bytes → 32 lowercase hex chars, cryptographically generated.
        Assert.Matches("^[0-9a-f]{32}$", initiation.GatewayReference);
        Assert.Equal($"/shop/{tenantId}/payments/sandbox/{initiation.GatewayReference}", initiation.RedirectUrl);

        // Exactly one attempt was persisted, in the Initiated state, and the
        // order itself has not yet moved off PendingPayment.
        Assert.Equal(1, await CountAttemptsForOrderAsync(orderId));
        Assert.Equal("Initiated", await GetAttemptStatusAsync(initiation.GatewayReference));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(orderId));
    }

    [Fact]
    public async Task Initiate_OrderAlreadyPaid_Returns409()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        var first = await PostInitiateAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var initiation = (await first.Content.ReadFromJsonAsync<InitiatePaymentDto>())!;

        // Resolve the order to Paid first so a second initiation has something
        // to refuse.
        var callback = await PostCallbackAsync(anonymous, tenantId, orderId, initiation.GatewayReference, approved: true);
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(orderId));

        var second = await PostInitiateAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("not awaiting payment", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_ApprovedWithValidReference_TransitionsOrderToPaid_AndAttemptSucceeded()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        var initiate = await PostInitiateAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.OK, initiate.StatusCode);
        var gatewayReference = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentDto>())!.GatewayReference;

        var response = await PostCallbackAsync(anonymous, tenantId, orderId, gatewayReference, approved: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var callback = (await response.Content.ReadFromJsonAsync<PaymentCallbackDto>())!;
        Assert.Equal(orderId, callback.OrderId);
        Assert.Equal("Paid", callback.Status);
        Assert.Equal("Paid", await GetOrderStatusAsync(orderId));
        Assert.Equal("Succeeded", await GetAttemptStatusAsync(gatewayReference));
    }

    [Fact]
    public async Task Callback_DeclinedWithValidReference_LeavesOrderPendingPayment_AndAttemptFailed()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        var initiate = await PostInitiateAsync(anonymous, tenantId, orderId);
        var gatewayReference = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentDto>())!.GatewayReference;

        var response = await PostCallbackAsync(anonymous, tenantId, orderId, gatewayReference, approved: false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var callback = (await response.Content.ReadFromJsonAsync<PaymentCallbackDto>())!;
        Assert.Equal(orderId, callback.OrderId);
        // A decline is not a failure of the order: it stays payable.
        Assert.Equal("PendingPayment", callback.Status);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(orderId));
        Assert.Equal("Failed", await GetAttemptStatusAsync(gatewayReference));
    }

    [Fact]
    public async Task Callback_UnknownGatewayReference_IsRejected_AndChangesNothing()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        // approved:true with an unknown reference → 409 (could not be verified).
        var approvedUnknown = await PostCallbackAsync(anonymous, tenantId, orderId, "does-not-exist-000000000000", approved: true);
        Assert.Equal(HttpStatusCode.Conflict, approvedUnknown.StatusCode);

        // approved:false with an unknown reference → 404 (no such attempt).
        var declinedUnknown = await PostCallbackAsync(anonymous, tenantId, orderId, "does-not-exist-000000000000", approved: false);
        Assert.Equal(HttpStatusCode.NotFound, declinedUnknown.StatusCode);

        // Nothing was persisted and the order is still payable.
        Assert.Equal(0, await CountAttemptsForOrderAsync(orderId));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(orderId));
    }

    [Fact]
    public async Task Callback_SecondCallbackForAnAlreadyResolvedAttempt_Returns409_AndIsNotReApplied()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();
        var initiate = await PostInitiateAsync(anonymous, tenantId, orderId);
        var gatewayReference = (await initiate.Content.ReadFromJsonAsync<InitiatePaymentDto>())!.GatewayReference;

        // First callback approves and resolves the order → Paid.
        var first = await PostCallbackAsync(anonymous, tenantId, orderId, gatewayReference, approved: true);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(orderId));

        // The exact same callback again: the attempt is already resolved and
        // the order is no longer PendingPayment, so it is rejected — the
        // attempt is never written a second time.
        var second = await PostCallbackAsync(anonymous, tenantId, orderId, gatewayReference, approved: true);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        Assert.Equal(1, await CountAttemptsForOrderAsync(orderId));
        Assert.Equal("Succeeded", await GetAttemptStatusAsync(gatewayReference));
        Assert.Equal("Paid", await GetOrderStatusAsync(orderId));
    }

    [Fact]
    public async Task Initiate_And_Callback_MalformedOrUnknownIds_Return404_AndMissingReference_Returns400()
    {
        var (tenantId, orderId, _) = await SetupPendingOrderAsync();
        var (otherTenant, _, _) = await SetupPendingOrderAsync();

        using var anonymous = CreateClient();

        // Malformed tenant / order ids → 404 on initiate, never 400/500.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, "not-a-tenant", orderId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, tenantId, "not-an-order")).StatusCode);
        // A well-formed order id that does not exist → 404.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, tenantId, TsidId.Format(TsidId.NewId()))).StatusCode);
        // An order under the wrong tenant → 404 (existence is scoped by the route tenantId).
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, otherTenant, orderId)).StatusCode);

        // A missing gatewayReference on the callback is a validation 400.
        var missingRef = await PostCallbackAsync(anonymous, tenantId, orderId, gatewayReference: null, approved: true);
        Assert.Equal(HttpStatusCode.BadRequest, missingRef.StatusCode);
        var body = await missingRef.Content.ReadAsStringAsync();
        Assert.Contains("gatewayReference", body, StringComparison.Ordinal);

        // The valid order is untouched by all of the above.
        Assert.Equal(0, await CountAttemptsForOrderAsync(orderId));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(orderId));
    }
}

internal sealed record InitiatePaymentDto(string GatewayReference, string RedirectUrl);

internal sealed record PaymentCallbackDto(string OrderId, string Status);

internal sealed record PaymentVariantDto(
    string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record PaymentProductDetailDto(
    string Id, string Name, string Slug, IReadOnlyList<PaymentVariantDto> Variants);

internal sealed record PaymentOrderCreatedDto(
    string OrderId, string OrderNumber, string TrackingCode, string Status,
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);
