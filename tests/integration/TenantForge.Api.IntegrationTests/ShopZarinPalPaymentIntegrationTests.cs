using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Payments.ZarinPal;
using TenantForge.Modules.Shop.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B045's ZarinPal provider integration: provider request/verify JSON,
/// checked IRT/IRR conversion, signed callback state, 100/101 verification
/// semantics, decline-without-verify, provider timeout behavior, secret-free
/// output/logging, and the no-new-migration persistence shape.
/// </summary>
[Collection(nameof(ShopZarinPalPaymentIsolatedCollection))]
public sealed class ShopZarinPalPaymentIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ShopZarinPalPaymentDbFixture _db;
    private readonly FakeZarinPalHttpMessageHandler _zarinPal = new();
    private readonly MutableTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-09-24T00:00:00Z"));
    private readonly ShopZarinPalApiFactory _factory;

    public ShopZarinPalPaymentIntegrationTests(ShopZarinPalPaymentDbFixture db)
    {
        _db = db;
        _factory = new ShopZarinPalApiFactory(db, _zarinPal, _timeProvider);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(bool allowAutoRedirect = true) => CreateClient(_factory, allowAutoRedirect);

    private static HttpClient CreateClient(ShopZarinPalApiFactory factory, bool allowAutoRedirect = true) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = allowAutoRedirect
    });

    internal sealed record B045InitiatePaymentResponse(string Provider, string RedirectUrl, string ResultToken);

    internal sealed record B045PaymentStatusResponse(string OrderNumber, string Status, string? ProviderReference);

    internal sealed record B045OrderCreatedDto(
        string OrderId, string OrderNumber, string TrackingCode, string Status,
        decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

    internal sealed record B045ProductDetailDto(string Id, string Name, string Slug, IReadOnlyList<B045VariantDto> Variants);

    internal sealed record B045VariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

    private sealed record Setup(
        string TenantId,
        string OrderId,
        string OrderNumber,
        decimal GrandTotal,
        HttpClient Owner,
        string ProductId,
        string CategoryId,
        string ProductSlug,
        string VariantId);

    // ─── Setup helpers ────────────────────────────────────────────────────────

    private async Task<HttpClient> PlatformAdminClientAsync(ShopZarinPalApiFactory? factory = null)
    {
        var client = factory is null ? CreateClient() : CreateClient(factory);
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
        await using var context = _db.CreateContext();
        var account = Account.CreateUser(email, "Shop Owner", "already-hashed-for-test", _timeProvider.GetUtcNow());
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<string> CreateTenantWithOwnerAsync(HttpClient adminClient, Tsid ownerAccountId, string name)
    {
        var response = await adminClient.PostAsJsonAsync("/api/platform/tenants", new
        {
            name,
            slug = $"zpal-{Guid.NewGuid():N}"[..18],
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

    private async Task<Setup> SetupPendingOrderAsync(decimal unitPrice = 100000m, int stockQuantity = 10)
        => await SetupPendingOrderAsync(_factory, unitPrice, stockQuantity);

    private async Task<Setup> SetupPendingOrderAsync(ShopZarinPalApiFactory factory, decimal unitPrice = 100000m, int stockQuantity = 10)
    {
        var admin = await PlatformAdminClientAsync(factory);
        var ownerAccount = await CreateOwnerAccountAsync($"zpal-owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"ZPal {Guid.NewGuid():N}"[..8]);
        var owner = CreateClient(factory);
        SetMemberToken(owner, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");

        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryResponse = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "ZarinPal Apparel",
            slug = $"zcat-{suffix}",
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        using var categoryDocument = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = categoryDocument.RootElement.GetProperty("id").GetString()!;

        var productSlug = $"zshirt-{suffix}";
        var productResponse = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "ZarinPal Shirt",
            slug = productSlug,
            description = (string?)null,
            categoryId,
            basePrice = unitPrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "Black", size = "M", sku = "B045", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        using var productDocument = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync());
        var productId = productDocument.RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 0m })).StatusCode);

        using var anonymous = CreateClient(factory);
        var cartResponse = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, cartResponse.StatusCode);
        using var cartDocument = JsonDocument.Parse(await cartResponse.Content.ReadAsStringAsync());
        var cartId = cartDocument.RootElement.GetProperty("cartId").GetString()!;

        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B045ProductDetailDto>())!;
        var variantId = detail.Variants[0].Id;

        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/carts/{cartId}/items",
                new { productVariantId = variantId, quantity = 1 })).StatusCode);

        var orderResponse = await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName = "مریم رضایی",
            customerPhone = "09121234567",
            shippingProvince = "Tehran",
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890",
            couponCode = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<B045OrderCreatedDto>())!;
        Assert.Equal("PendingPayment", order.Status);

        return new Setup(tenantId, order.OrderId, order.OrderNumber, order.GrandTotal,
            owner, productId, categoryId, productSlug, variantId);
    }

    private static async Task<HttpResponseMessage> PostInitiateAsync(HttpClient anonymous, string tenantId, string orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/shop/{tenantId}/orders/{orderId}/payments/initiate");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return await anonymous.SendAsync(request);
    }

    private static async Task<B045InitiatePaymentResponse> InitiateAsync(HttpClient anonymous, string tenantId, string orderId)
    {
        var response = await PostInitiateAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<B045InitiatePaymentResponse>())!;
    }

    private static Uri ExtractCallbackUri(FakeZarinPalHttpMessageHandler handler)
    {
        var requestJson = Assert.Single(handler.RequestBodies);
        using var document = JsonDocument.Parse(requestJson);
        return new Uri(document.RootElement.GetProperty("callbackUrl").GetString()!);
    }

    private async Task<HttpResponseMessage> GetCallbackAsync(Uri callbackUri, string status = "OK", string? authority = null)
    {
        using var client = CreateClient(allowAutoRedirect: false);
        var builder = new UriBuilder(callbackUri);
        var separator = string.IsNullOrWhiteSpace(builder.Query) ? "" : "&";
        builder.Query = builder.Query.TrimStart('?') + separator
            + $"Authority={Uri.EscapeDataString(authority ?? "A000000000000000000000000000000001")}&Status={Uri.EscapeDataString(status)}";
        return await client.GetAsync(builder.Uri.PathAndQuery);
    }

    private async Task<string> GetOrderStatusAsync(string orderId)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid));
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select status from shop_orders where id = @p;";
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(string Id, string Status, string GatewayReference, string? ProviderReference, string? FailureCode, decimal AmountSnapshot, string? VerifiedAtUtc, int Version)> ReadAttemptAsync(string orderId)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid));
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select id, status, gateway_reference, provider_reference, failure_code, amount_snapshot, verified_at_utc::text, version
            from shop_payment_attempts
            where order_id = @p
            order by created_at_utc, id
            limit 1;
            """;
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "one attempt expected");
        return (
            TsidId.Format(Tsid.From(reader.GetInt64(0))),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            Convert.ToDecimal(reader.GetValue(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7));
    }

    private async Task<string> ReadRawPaymentAttemptColumnAsync(string orderId, string columnName)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid));
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select {columnName}::text from shop_payment_attempts where order_id = @p order by created_at_utc, id limit 1;";
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task SeedSuccessfulZarinPalAttemptAsync(Setup setup, string authority, string providerReference, string callbackToken)
    {
        Assert.True(TsidId.TryParse(setup.TenantId, out var tenantTsid));
        Assert.True(TsidId.TryParse(setup.OrderId, out var orderTsid));
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql(_db.ConnectionString)
            .Options;
        await using var shop = new ShopDbContext(options);
        var attempt = ShopPaymentAttempt.Create(
            orderTsid,
            "ZarinPal",
            authority,
            setup.GrandTotal,
            Sha256Hex(callbackToken),
            _timeProvider.GetUtcNow());
        Assert.True(attempt.TryResolve(true, providerReference, null, _timeProvider.GetUtcNow()));
        shop.PaymentAttempts.Add(attempt);
        shop.PaymentInitiations.Add(ShopPaymentInitiation.Create(
            tenantTsid,
            orderTsid,
            attempt.Id,
            Guid.NewGuid().ToString(),
            Sha256Hex(setup.TenantId + "|" + setup.OrderId),
            "https://dev.zarinpal.com/payment/" + authority,
            RandomNumberGenerator.GetBytes(32),
            _timeProvider.GetUtcNow()));
        var order = await shop.Orders.SingleAsync(o => o.Id == orderTsid);
        order.MarkPaid();
        await shop.SaveChangesAsync();
    }

    private static async Task<string> CanonicalProblemBodyAsync(HttpResponseMessage response)
    {
        var body = (JsonObject?)JsonSerializer.Deserialize<JsonNode>(await response.Content.ReadAsStringAsync(), Json)!;
        body.Remove("traceId");
        return body.ToJsonString(Json);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_ZarinPal_SendsExpectedRequestJson_ReturnsProviderRedirect_AndPersistsAuthority()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000001");
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        Assert.Equal("ZarinPal", initiation.Provider);
        Assert.Equal("https://dev.zarinpal.com/payment/A000000000000000000000000000000001", initiation.RedirectUrl);
        Assert.Matches("^[0-9a-f]{64}$", initiation.ResultToken);
        Assert.Single(_zarinPal.RequestUris, uri => uri.AbsolutePath == "/v4/payment/request");

        using var document = JsonDocument.Parse(Assert.Single(_zarinPal.RequestBodies));
        var root = document.RootElement;
        Assert.Equal("test-merchant-id-secret", root.GetProperty("merchantId").GetString());
        Assert.Equal("100000", root.GetProperty("amount").GetString());
        Assert.Equal("IRT", root.GetProperty("currency").GetString());
        Assert.Contains(setup.OrderId, root.GetProperty("description").GetString(), StringComparison.Ordinal);
        var callback = new Uri(root.GetProperty("callbackUrl").GetString()!);
        Assert.Equal("/api/shop/" + setup.TenantId + "/payments/zarinpal/callback", callback.AbsolutePath);
        Assert.DoesNotContain(setup.OrderId, callback.Query, StringComparison.Ordinal);
        Assert.Contains("state=", callback.Query, StringComparison.Ordinal);

        var attempt = await ReadAttemptAsync(setup.OrderId);
        Assert.Equal("Initiated", attempt.Status);
        Assert.Equal("A000000000000000000000000000000001", attempt.GatewayReference);
        Assert.Equal(setup.GrandTotal, attempt.AmountSnapshot);
        Assert.Null(attempt.ProviderReference);
        Assert.Null(attempt.FailureCode);
    }

    [Theory]
    [InlineData("IRT", "100000")]
    [InlineData("IRR", "1000000")]
    public async Task Initiate_CurrencyConfiguration_ConvertsAmountExactlyOnce(string currency, string expectedProviderAmount)
    {
        using var factory = new ShopZarinPalApiFactory(_db, _zarinPal, _timeProvider, currency: currency);
        using var client = CreateClient(factory);
        _zarinPal.EnqueueRequestResponse(100, currency == "IRT"
            ? "A000000000000000000000000000000002"
            : "A000000000000000000000000000000016");
        var setup = await SetupPendingOrderAsync(factory);

        _ = await InitiateAsync(client, setup.TenantId, setup.OrderId);

        using var document = JsonDocument.Parse(Assert.Single(_zarinPal.RequestBodies));
        Assert.Equal(expectedProviderAmount, document.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Callback_VerifyCode100_MarksPaymentSucceeded_AndPersistsRefId()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000003");
        _zarinPal.EnqueueVerifyResponse(100, 424242);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var callback = await GetCallbackAsync(ExtractCallbackUri(_zarinPal));

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("outcome=approved", callback.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));
        var attempt = await ReadAttemptAsync(setup.OrderId);
        Assert.Equal("Succeeded", attempt.Status);
        Assert.Equal("424242", attempt.ProviderReference);
        Assert.Null(attempt.FailureCode);
    }

    [Fact]
    public async Task Callback_VerifyCode101_WithMatchingPriorSuccess_ReplaysApprovedOutcome()
    {
        var setup = await SetupPendingOrderAsync();
        await SeedSuccessfulZarinPalAttemptAsync(setup, "A000000000000000000000000000000004", "777", "callback-token-101-ok");
        using var scope = _factory.Services.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<ZarinPalCallbackStateProtector>().Protect(new ZarinPalCallbackState(
            setup.TenantId, setup.OrderId, (await ReadAttemptAsync(setup.OrderId)).Id, "callback-token-101-ok"));
        _zarinPal.EnqueueVerifyResponse(101, 777);
        using var client = CreateClient(allowAutoRedirect: false);

        var response = await client.GetAsync($"/api/shop/{setup.TenantId}/payments/zarinpal/callback?state={Uri.EscapeDataString(state)}&Status=OK&Authority=ignored");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("outcome=approved", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(1, _zarinPal.VerifyCallCount);
    }

    [Fact]
    public async Task Callback_VerifyCode101_WithoutMatchingStoredSuccess_FailsClosed()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000005");
        _zarinPal.EnqueueVerifyResponse(101, 888);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var response = await GetCallbackAsync(ExtractCallbackUri(_zarinPal));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("outcome=declined", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        var attempt = await ReadAttemptAsync(setup.OrderId);
        Assert.Equal("Failed", attempt.Status);
        Assert.Equal("already_verified_mismatch", attempt.FailureCode);
    }

    [Fact]
    public async Task Callback_StatusNotOk_DeclinesWithoutCallingVerify()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000006");
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var response = await GetCallbackAsync(ExtractCallbackUri(_zarinPal), status: "NOK");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("outcome=declined", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, _zarinPal.VerifyCallCount);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("payment_declined", (await ReadAttemptAsync(setup.OrderId)).FailureCode);
    }

    [Fact]
    public async Task Callback_OtherVerifyError_FailsPayment()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000007");
        _zarinPal.EnqueueVerifyResponse(-50, null);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var response = await GetCallbackAsync(ExtractCallbackUri(_zarinPal));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("verification_failed", (await ReadAttemptAsync(setup.OrderId)).FailureCode);
    }

    [Fact]
    public async Task Callback_MismatchedAuthorityInQuery_IsIgnored_VerificationUsesStoredAuthorityAndAmount()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000008");
        _zarinPal.EnqueueVerifyResponse(100, 123456);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var response = await GetCallbackAsync(ExtractCallbackUri(_zarinPal), authority: "FORGED-AUTHORITY");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));
        using var verify = JsonDocument.Parse(Assert.Single(_zarinPal.VerifyBodies));
        Assert.Equal("A000000000000000000000000000000008", verify.RootElement.GetProperty("authority").GetString());
        Assert.Equal("100000", verify.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Callback_TamperedState_ReturnsGeneric404()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000009");
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var callback = ExtractCallbackUri(_zarinPal);
        var tampered = callback.Query.Replace("state=", "state=x");

        var response = await anonymous.GetAsync($"/api/shop/{setup.TenantId}/payments/zarinpal/callback{tampered}&Status=OK&Authority=anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("Initiated", (await ReadAttemptAsync(setup.OrderId)).Status);
    }

    [Fact]
    public async Task Callback_ExpiredState_ReturnsGeneric404()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000010");
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var callback = ExtractCallbackUri(_zarinPal);
        _timeProvider.Advance(TimeSpan.FromMinutes(31));

        var response = await GetCallbackAsync(callback);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("Initiated", (await ReadAttemptAsync(setup.OrderId)).Status);
    }

    [Fact]
    public async Task Callback_SameSuccessfulCallbackTwice_IsIdempotent_NoDoubleSideEffects()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000011");
        _zarinPal.EnqueueVerifyResponse(100, 111111);
        _zarinPal.EnqueueVerifyResponse(101, 111111);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var callback = ExtractCallbackUri(_zarinPal);

        var first = await GetCallbackAsync(callback);
        var afterFirst = await ReadAttemptAsync(setup.OrderId);
        var second = await GetCallbackAsync(callback);
        var afterSecond = await ReadAttemptAsync(setup.OrderId);

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("Succeeded", afterSecond.Status);
        Assert.Equal(afterFirst.VerifiedAtUtc, afterSecond.VerifiedAtUtc);
        Assert.Equal(afterFirst.Version, afterSecond.Version);
    }

    [Fact]
    public async Task Callback_ProviderTimeout_Returns503_AndAttemptStaysInitiated()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000012");
        _zarinPal.FailNextVerifyWithTimeout = true;
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var response = await GetCallbackAsync(ExtractCallbackUri(_zarinPal));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        var attempt = await ReadAttemptAsync(setup.OrderId);
        Assert.Equal("Initiated", attempt.Status);
        Assert.Null(attempt.FailureCode);
        Assert.Null(attempt.VerifiedAtUtc);
    }

    [Fact]
    public async Task Callback_LateSuccessAfterCancel_DoesNotMarkOrderPaid()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000013");
        _zarinPal.EnqueueVerifyResponse(100, 131313);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var callback = ExtractCallbackUri(_zarinPal);
        var cancel = new HttpRequestMessage(HttpMethod.Patch, $"/api/tenants/{setup.TenantId}/shop/orders/{setup.OrderId}/status")
        {
            Content = JsonContent.Create(new { action = "Cancel", expectedVersion = 1 })
        };
        cancel.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await setup.Owner.SendAsync(cancel)).StatusCode);

        var response = await GetCallbackAsync(callback);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("outcome=declined", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal("Cancelled", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal("Invalidated", (await ReadAttemptAsync(setup.OrderId)).Status);
    }

    [Fact]
    public void ProductionWithUnsafeZarinPalUrl_FailsClosedAtStartup()
    {
        using var factory = new ShopUnsafeZarinPalProductionApiFactory(_db);
        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });
        Assert.Contains("HTTPS", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponsesAndLogs_DoNotLeakMerchantIdOrCardPan()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000014");
        _zarinPal.EnqueueVerifyResponse(100, 141414, cardPan: "621986******1234");
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient(allowAutoRedirect: false);
        var initiationResponse = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var initiationBody = await initiationResponse.Content.ReadAsStringAsync();
        var callback = await GetCallbackAsync(ExtractCallbackUri(_zarinPal));
        var callbackBody = await callback.Content.ReadAsStringAsync();

        var combined = initiationBody + callbackBody + string.Join("\n", _factory.LogCollector.Entries);
        Assert.DoesNotContain("test-merchant-id-secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("621986", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("1234", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Persistence_StoresAuthorityInGatewayReference_AndRefIdInProviderReference()
    {
        _zarinPal.EnqueueRequestResponse(100, "A000000000000000000000000000000015");
        _zarinPal.EnqueueVerifyResponse(100, 151515);
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        _ = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        await GetCallbackAsync(ExtractCallbackUri(_zarinPal));

        Assert.Equal("A000000000000000000000000000000015", await ReadRawPaymentAttemptColumnAsync(setup.OrderId, "gateway_reference"));
        Assert.Equal("151515", await ReadRawPaymentAttemptColumnAsync(setup.OrderId, "provider_reference"));
    }
}

internal sealed class FakeZarinPalHttpMessageHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Queue<string> _requestResponses = new();
    private readonly Queue<string> _verifyResponses = new();

    public List<Uri> RequestUris { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public List<string> VerifyBodies { get; } = [];
    public bool FailNextVerifyWithTimeout { get; set; }
    public int VerifyCallCount => VerifyBodies.Count;

    public void EnqueueRequestResponse(int code, string authority, string? message = null)
    {
        _requestResponses.Enqueue(JsonSerializer.Serialize(new { code = code.ToString(), authority, message }, SerializerOptions));
    }

    public void EnqueueVerifyResponse(int code, long? refId, string? cardPan = null, string? message = null)
    {
        _verifyResponses.Enqueue(JsonSerializer.Serialize(new { code = code.ToString(), refId = refId?.ToString(), cardPan, message }, SerializerOptions));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri!);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (request.RequestUri!.AbsolutePath.EndsWith("/request", StringComparison.Ordinal))
        {
            RequestBodies.Add(body);
            return JsonResponse(_requestResponses.Count == 0
                ? JsonSerializer.Serialize(new { code = "100", authority = "A-default" }, SerializerOptions)
                : _requestResponses.Dequeue());
        }

        if (request.RequestUri!.AbsolutePath.EndsWith("/verify", StringComparison.Ordinal))
        {
            VerifyBodies.Add(body);
            if (FailNextVerifyWithTimeout)
            {
                FailNextVerifyWithTimeout = false;
                throw new TaskCanceledException("simulated timeout");
            }

            return JsonResponse(_verifyResponses.Count == 0
                ? JsonSerializer.Serialize(new { code = "100", refId = "1" }, SerializerOptions)
                : _verifyResponses.Dequeue());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

internal sealed class ShopZarinPalApiFactory(
    IamDbFixtureBase db,
    FakeZarinPalHttpMessageHandler handler,
    TimeProvider timeProvider,
    string currency = "IRT") : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-zarinpal-tests", Guid.NewGuid().ToString("N"));

    public TestLogCollector LogCollector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Development");

        var values = BaseValues(_contentRoot, db.ConnectionString, currency);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(timeProvider);
            services.ConfigureHttpClientDefaults(http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        builder.ConfigureLogging((_, logging) => logging.AddProvider(new CollectorLoggerProvider(LogCollector)));
    }

    internal static Dictionary<string, string?> BaseValues(string contentRoot, string connectionString, string currency = "IRT") => new()
    {
        ["Logging:LogLevel:Default"] = "Debug",
        ["AllowedOrigins:0"] = "http://localhost:5173",
        ["IAM:IamDb"] = connectionString,
        ["IAM:Auth:SigningKey"] = ApiFactory.SigningKey,
        ["IAM:SeedAdmin:Email"] = ApiFactory.Email,
        ["IAM:SeedAdmin:Password"] = ApiFactory.Password,
        ["IAM:SeedAdmin:DisplayName"] = ApiFactory.DisplayName,
        ["Shop:ShopDb"] = connectionString,
        ["Shop:MediaRoot"] = Path.Combine(contentRoot, "shop-media"),
        ["Shop:CartReservationMinutes"] = "30",
        ["Shop:CartCleanupIntervalSeconds"] = "3600",
        ["Shop:Payments:Provider"] = "ZarinPal",
        ["Shop:Payments:ZarinPal:MerchantId"] = "test-merchant-id-secret",
        ["Shop:Payments:ZarinPal:Currency"] = currency,
        ["Shop:Payments:ZarinPal:RequestEndpoint"] = "http://zarinpal.test/v4/payment/request",
        ["Shop:Payments:ZarinPal:VerifyEndpoint"] = "http://zarinpal.test/v4/payment/verify",
        ["Shop:Payments:ZarinPal:GatewayBaseUrl"] = "https://dev.zarinpal.com/payment/",
        ["Shop:Payments:ZarinPal:PublicApiBaseUrl"] = "https://api.tenantforge.local",
        ["Shop:Payments:ZarinPal:FrontendResultBaseUrl"] = "https://frontend.tenantforge.local",
        ["Shop:Payments:ZarinPal:TimeoutSeconds"] = "10",
        // B046: the rate-limit body-size middleware resolves these options during
        // pipeline setup, so a Production host (the unsafe-URL factory reuses
        // this base) must carry them too. Development hosts use them as their
        // explicit defaults.
        ["Shop:RateLimiting:OrderLookupPerMinute"] = "30",
        ["Shop:RateLimiting:CartMutationPerMinute"] = "120",
        ["Shop:RateLimiting:CheckoutOrderPerMinute"] = "30",
        ["Shop:RateLimiting:PaymentInitiationPerMinute"] = "20",
        ["Shop:RateLimiting:MaxRequestBodyBytes"] = "524288"
    };

    public new void Dispose()
    {
        base.Dispose();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class ShopUnsafeZarinPalProductionApiFactory(IamDbFixtureBase db)
    : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-zarinpal-unsafe-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Production");
        var values = ShopZarinPalApiFactory.BaseValues(_contentRoot, db.ConnectionString);
        values["Shop:Payments:ZarinPal:RequestEndpoint"] = "http://api.zarinpal.com/v4/payment/request";
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(values));
    }

    public new void Dispose()
    {
        base.Dispose();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
