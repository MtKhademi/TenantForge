using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B044's gateway-neutral, idempotent payment lifecycle on the three
/// anonymous routes:
///
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate
///       (Idempotency-Key UUID header, no body)
///   GET  /api/shop/{tenantId}/orders/{orderId}/payments/status?token=…
///   POST /api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve
///       (Development only)
///
/// and the seams behind them: the <c>IShopPaymentGateway</c> contract, the
/// resolver that picks a gateway by <c>Shop:Payments:Provider</c>, and
/// <c>ShopPaymentCompletionService</c> as the ONLY place an attempt leaves
/// <c>Initiated</c> or an order moves to <c>Paid</c>. The old browser-authored
/// <c>payments/callback</c> route is gone; the sandbox resolve is the only
/// browser-driven simulation and the browser never declares success — it only
/// carries the authority it was redirected with.
///
/// The raw callback token is never persisted: only its SHA-256 is, and
/// <c>GET …/payments/status</c> is the only route that accepts it (compared
/// to the stored hash in fixed time). Every assertion reads the real
/// PostgreSQL rows back through raw SQL — a status-code-only test would not be
/// sufficient.
///
/// Runs on a dedicated database (ShopPaymentLifecycleIsolatedCollection) so
/// its tenants, orders, attempts and initiation rows never inflate the other
/// Shop databases.
/// </summary>
[Collection(nameof(ShopPaymentLifecycleIsolatedCollection))]
public sealed class ShopPaymentLifecycleIntegrationTests(ShopPaymentLifecycleDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    // ─── Wire DTOs (B044L-prefixed: the assembly is one namespace) ────────────

    internal sealed record B044LInitiatePaymentResponse(string Provider, string RedirectUrl, string ResultToken);

    internal sealed record B044LPaymentStatusResponse(string OrderNumber, string Status, string? ProviderReference);

    internal sealed record B044LProblemDto(string Type, string Title, string? Detail);

    internal sealed record B044LVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

    internal sealed record B044LProductDetailDto(
        string Id, string Name, string Slug, IReadOnlyList<B044LVariantDto> Variants);

    internal sealed record B044LOrderCreatedDto(
        string OrderId, string OrderNumber, string TrackingCode, string Status,
        decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

    private sealed record Setup(
        string TenantId, string OrderId, string OrderNumber, decimal GrandTotal,
        HttpClient Owner, string ProductId, string CategoryId, string ProductSlug, string VariantId);

    // ─── Setup helpers ─────────────────────────────────────────────────────────

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

    /// <summary>
    /// Authors a tenant, a single-variant product at <paramref name="unitPrice"/>
    /// (stock <paramref name="stockQuantity"/>), a zero-cost Tehran rate, a cart
    /// and a real PendingPayment order through the anonymous flow — exactly the
    /// state the payment lifecycle starts from.
    /// </summary>
    private async Task<Setup> SetupPendingOrderAsync(decimal unitPrice = 100000m, int stockQuantity = 10)
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var owner = CreateClient();
        SetMemberToken(owner, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");

        var suffix = Guid.NewGuid().ToString("N")[..14];

        var categoryResponse = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Apparel",
            slug = $"cat-{suffix}",
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        using var categoryDocument = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = categoryDocument.RootElement.GetProperty("id").GetString()!;

        var productSlug = $"shirt-{suffix}";
        var productResponse = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Shirt",
            slug = productSlug,
            description = (string?)null,
            categoryId,
            basePrice = unitPrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "Black", size = "M", sku = "B044", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, productResponse.StatusCode);
        using var productDocument = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync());
        var productId = productDocument.RootElement.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 0m })).StatusCode);

        using var anonymous = CreateClient();
        var cartResponse = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, cartResponse.StatusCode);
        using var cartDocument = JsonDocument.Parse(await cartResponse.Content.ReadAsStringAsync());
        var cartId = cartDocument.RootElement.GetProperty("cartId").GetString()!;

        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B044LProductDetailDto>())!;
        Assert.Single(detail.Variants);
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
        var order = (await orderResponse.Content.ReadFromJsonAsync<B044LOrderCreatedDto>())!;
        Assert.Equal("PendingPayment", order.Status);

        return new Setup(tenantId, order.OrderId, order.OrderNumber, order.GrandTotal,
            owner, productId, categoryId, productSlug, variantId);
    }

    // ─── The endpoints under test ──────────────────────────────────────────────

    private static async Task<HttpResponseMessage> PostInitiateAsync(
        HttpClient anonymous, string tenantId, string orderId, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/shop/{tenantId}/orders/{orderId}/payments/initiate");
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await anonymous.SendAsync(request);
    }

    private static async Task<B044LInitiatePaymentResponse> InitiateAsync(
        HttpClient anonymous, string tenantId, string orderId, string? idempotencyKey = null)
    {
        var response = await PostInitiateAsync(anonymous, tenantId, orderId, idempotencyKey ?? Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<B044LInitiatePaymentResponse>())!;
    }

    private static string ParseAuthorityFromRedirectUrl(string redirectUrl)
    {
        // /shop/{tenantId}/bank?authority={authority}
        var index = redirectUrl.IndexOf("authority=", StringComparison.Ordinal);
        Assert.True(index >= 0, $"redirectUrl must carry an authority: {redirectUrl}");
        return redirectUrl[(index + "authority=".Length)..];
    }

    private static Task<HttpResponseMessage> ResolveSandboxAsync(
        HttpClient anonymous, string tenantId, string orderId, string? authority, bool approved)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve",
            new { authority, approved });

    private static Task<HttpResponseMessage> GetStatusAsync(
        HttpClient anonymous, string tenantId, string orderId, string? token)
        => anonymous.GetAsync(
            $"/api/shop/{tenantId}/orders/{orderId}/payments/status?token={Uri.EscapeDataString(token ?? "")}");

    // ─── Raw-SQL persistence readers ───────────────────────────────────────────

    private async Task<int> CountAttemptsAsync(string orderId, string? status = null)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid), "orderId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = status is null
            ? "select count(*) from shop_payment_attempts where order_id = @p;"
            : "select count(*) from shop_payment_attempts where order_id = @p and status = @status;";
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        if (status is not null)
        {
            command.Parameters.Add(new NpgsqlParameter("@status", status));
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<string> GetOrderStatusAsync(string orderId)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid), "orderId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "select status from shop_orders where id = @p;";
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        var result = await command.ExecuteScalarAsync();
        return (string)result!;
    }

    private async Task<(decimal AmountSnapshot, string CallbackTokenHash, string? FailureCode, string? VerifiedAtUtc)>
        ReadAttemptRowAsync(string orderId)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid), "orderId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT amount_snapshot, callback_token_hash, failure_code, verified_at_utc::text
            FROM shop_payment_attempts WHERE order_id = @p ORDER BY created_at_utc, id;
            """;
        command.Parameters.Add(new NpgsqlParameter("@p", orderTsid.ToLong()));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "exactly one attempt expected");
        return (
            Convert.ToDecimal(reader.GetValue(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// The one generic 404 body the status route must return for every miss —
    /// compared after stripping only the per-request traceId, so nothing else
    /// may differ between cases.
    /// </summary>
    private static async Task<string> CanonicalProblemBodyAsync(HttpResponseMessage response)
    {
        var body = (JsonObject?)JsonSerializer.Deserialize<JsonNode>(await response.Content.ReadAsStringAsync(), Json)!;
        body.Remove("traceId");
        return body.ToJsonString(Json);
    }

    // ─── 1. Valid initiation ────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_ValidPendingOrder_Returns200_SandboxProvider_BankRedirect_AndPersistsInitiatedAttempt()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization); // route is anonymous

        var response = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var initiation = (await response.Content.ReadFromJsonAsync<B044LInitiatePaymentResponse>())!;
        Assert.Equal("Sandbox", initiation.Provider);
        // 16 bytes → 32 lowercase hex chars, cryptographically generated.
        var authority = ParseAuthorityFromRedirectUrl(initiation.RedirectUrl);
        Assert.Matches("^[0-9a-f]{32}$", authority);
        // 32 bytes → 64 lowercase hex chars.
        Assert.Matches("^[0-9a-f]{64}$", initiation.ResultToken);

        // Exactly one Initiated attempt persisted, the order still payable, and
        // only the token's hash is stored.
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Initiated"));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        var row = await ReadAttemptRowAsync(setup.OrderId);
        Assert.Equal(setup.GrandTotal, row.AmountSnapshot);
        Assert.Equal(Sha256Hex(initiation.ResultToken), row.CallbackTokenHash);
        Assert.Null(row.FailureCode);
        Assert.Null(row.VerifiedAtUtc);
    }

    // ─── 2. Same-key retry replays byte-identically ────────────────────────────

    [Fact]
    public async Task Initiate_SameIdempotencyKeyAndRequest_ReplaysByteIdenticalResponse_NoDuplicateAttempt()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var key = Guid.NewGuid().ToString();
        var first = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = await first.Content.ReadAsStringAsync();
        var firstBody = JsonDocument.Parse(firstJson).RootElement.Deserialize<B044LInitiatePaymentResponse>(Json)!;

        var second = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, key);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondJson = await second.Content.ReadAsStringAsync();

        // Byte-for-byte identical: same provider, same stored redirect URL and
        // the same result token re-derived from the stored seed.
        Assert.Equal(firstJson, secondJson);

        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId)); // one attempt row
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Initiated"));

        // A fresh key for the order that now has a live attempt reuses it —
        // no second live attempt is ever minted (no orphan rows).
        var third = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        Assert.Equal(firstBody.RedirectUrl, third.RedirectUrl);
        Assert.Equal(firstBody.ResultToken, third.ResultToken);
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId));
    }



    // ─── 3. Two initiations, no replay → one live attempt ──────────────────────

    [Fact]
    public async Task Initiate_TwiceWithDifferentKeys_ReturnsTheSameLiveAttempt_OnceOnly()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var first = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var second = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        Assert.Equal(first.RedirectUrl, second.RedirectUrl);
        Assert.Equal(first.ResultToken, second.ResultToken);
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Initiated"));
    }

    // ─── 4. AmountSnapshot is frozen at initiation ─────────────────────────────

    [Fact]
    public async Task Attempt_AmountSnapshot_MatchesInitiationTime_EvenAfterTheCatalogPriceChanges()
    {
        var setup = await SetupPendingOrderAsync(unitPrice: 100000m);
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        // The frozen amount equals the order's total at initiation time.
        Assert.Equal(setup.GrandTotal, (await ReadAttemptRowAsync(setup.OrderId)).AmountSnapshot);

        // Wholesale-edit the live catalog: the price doubles. The attempt was
        // frozen before this, so its snapshot must not move.
        var rename = await setup.Owner.PutAsJsonAsync(
            $"/api/tenants/{setup.TenantId}/shop/products/{setup.ProductId}", new
            {
                name = "Premium Renamed Shirt",
                slug = setup.ProductSlug,
                description = (string?)null,
                categoryId = setup.CategoryId,
                basePrice = 200000,
                compareAtPrice = (decimal?)null,
                isActive = true,
                variants = new object[] { new { color = "Black", size = "M", sku = "B044", stockQuantity = 9, priceOverride = (decimal?)null } },
                sizeGuideColumns = (object?)null,
                sizeGuideRows = (object?)null
            });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // Pay the order through the sandbox resolve (the Development path).
        var resolve = await ResolveSandboxAsync(
            anonymous, setup.TenantId, setup.OrderId, ParseAuthorityFromRedirectUrl(initiation.RedirectUrl), approved: true);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));

        var row = await ReadAttemptRowAsync(setup.OrderId);
        Assert.Equal(setup.GrandTotal, row.AmountSnapshot); // still the initiation-time amount
    }

    // ─── 5. The 10-attempt cap ─────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_EleventhAttempt_IsRejected_AsTooMany_AttemptCountStaysTen()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        for (var i = 0; i < 10; i++)
        {
            // A fresh key each round: each minted attempt is declined so the
            // order stays payable and no live attempt blocks the next key.
            var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
            var decline = await ResolveSandboxAsync(
                anonymous, setup.TenantId, setup.OrderId,
                ParseAuthorityFromRedirectUrl(initiation.RedirectUrl), approved: false);
            Assert.Equal(HttpStatusCode.OK, decline.StatusCode);
            Assert.Equal(i + 1, await CountAttemptsAsync(setup.OrderId, "Failed"));
        }

        Assert.Equal(10, await CountAttemptsAsync(setup.OrderId));

        var eleventh = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, eleventh.StatusCode);
        var problem = (await eleventh.Content.ReadFromJsonAsync<B044LProblemDto>())!;
        Assert.Equal("too_many_payment_attempts", problem.Type);

        Assert.Equal(10, await CountAttemptsAsync(setup.OrderId));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
    }

    // ─── 6. Successful verification pays the order ─────────────────────────────

    [Fact]
    public async Task SandboxResolve_Approved_MovesOrderToPaid_AndAttemptToSucceeded()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var resolve = await ResolveSandboxAsync(
            anonymous, setup.TenantId, setup.OrderId, ParseAuthorityFromRedirectUrl(initiation.RedirectUrl), approved: true);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var body = (await resolve.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!;
        Assert.Equal(setup.OrderNumber, body.OrderNumber);
        Assert.Equal("Paid", body.Status);

        Assert.Equal("Paid", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Succeeded"));
        var row = await ReadAttemptRowAsync(setup.OrderId);
        Assert.NotNull(row.VerifiedAtUtc);
        Assert.Null(row.FailureCode);
    }

    // ─── 7. Failed verification leaves the order payable ───────────────────────

    [Fact]
    public async Task SandboxResolve_Declined_KeepsOrderPendingPayment_AndAttemptFailed_WithStableCode()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var resolve = await ResolveSandboxAsync(
            anonymous, setup.TenantId, setup.OrderId, ParseAuthorityFromRedirectUrl(initiation.RedirectUrl), approved: false);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var body = (await resolve.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!;
        Assert.Equal("PendingPayment", body.Status);

        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Failed"));
        var row = await ReadAttemptRowAsync(setup.OrderId);
        Assert.NotNull(row.VerifiedAtUtc);
        Assert.Equal("payment_declined", row.FailureCode);
    }

    // ─── 8. Duplicate success returns the stored outcome, no re-apply ─────────

    [Fact]
    public async Task SandboxResolve_SecondSuccess_ReturnsStoredOutcome_AndDoesNotReApply()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var authority = ParseAuthorityFromRedirectUrl(initiation.RedirectUrl);

        var first = await ResolveSandboxAsync(anonymous, setup.TenantId, setup.OrderId, authority, approved: true);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = (await first.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!;
        Assert.Equal("Paid", firstBody.Status);
        var afterFirst = await ReadAttemptRowAsync(setup.OrderId);

        // The same success again: the attempt is already resolved, so the
        // completion service returns the already-computed outcome instead of
        // re-running the transition.
        var second = await ResolveSandboxAsync(anonymous, setup.TenantId, setup.OrderId, authority, approved: true);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = (await second.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!;
        Assert.Equal(firstBody, secondBody);

        var afterSecond = await ReadAttemptRowAsync(setup.OrderId);
        // Nothing was re-written: same version, same verification stamp, and
        // still exactly one Succeeded attempt.
        Assert.Equal(afterFirst.VerifiedAtUtc, afterSecond.VerifiedAtUtc);
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Succeeded"));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId));
    }

    // ─── 9. A late success for a cancelled order never pays it ─────────────────

    [Fact]
    public async Task LateSuccessForACancelledOrder_ReturnsCancelledOutcome_AndNeverPays()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var authority = ParseAuthorityFromRedirectUrl(initiation.RedirectUrl);

        // Move the order out of PendingPayment through B043's operator route.
        // Cancelling also invalidates the live attempt (B044) inside the same
        // transaction, so the order and its attempt cannot drift apart.
        var key = Guid.NewGuid().ToString();
        var cancel = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/tenants/{setup.TenantId}/shop/orders/{setup.OrderId}/status")
        {
            Content = JsonContent.Create(new { action = "Cancel", expectedVersion = 1 })
        };
        cancel.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await setup.Owner.SendAsync(cancel)).StatusCode);
        Assert.Equal("Cancelled", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Invalidated"));

        // A gateway whose success notification raced the cancel still holds the
        // authority and reports success. The attempt is no longer Initiated, so
        // the completion service returns the already-computed outcome — the
        // order's current Cancelled status — instead of re-running the
        // transition. The order never moves to Paid and the attempt is never
        // re-resolved to Succeeded.
        var late = await ResolveSandboxAsync(anonymous, setup.TenantId, setup.OrderId, authority, approved: true);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var lateBody = (await late.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!;
        Assert.Equal("Cancelled", lateBody.Status);

        Assert.Equal("Cancelled", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(0, await CountAttemptsAsync(setup.OrderId, "Succeeded"));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Invalidated"));
    }

    // ─── 10. Only the token hash is stored ─────────────────────────────────────

    [Fact]
    public async Task StoredAttempt_ContainsOnlySha256OfTheRawToken_NeverTheRawTokenItself()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);

        var row = await ReadAttemptRowAsync(setup.OrderId);
        // The stored hash is exactly the SHA-256 of the raw token.
        Assert.Equal(Sha256Hex(initiation.ResultToken), row.CallbackTokenHash);
        // The raw token itself is not what is stored.
        Assert.NotEqual(initiation.ResultToken, row.CallbackTokenHash);
        // The raw token is 64 hex chars, like a hash — assert it is not stored
        // verbatim anywhere the hash is, and that re-hashing the stored value
        // does not reproduce it (proof it is not a raw token that was hashed
        // twice).
        Assert.NotEqual(row.CallbackTokenHash, Sha256Hex(row.CallbackTokenHash));
    }

    // ─── 11. Status endpoint: one generic 404 for every miss ───────────────────

    [Fact]
    public async Task Status_WrongToken_WrongOrder_WrongTenant_AndNoToken_AllReturnTheSameGeneric404()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var tenantB = await NewTenantForIsolationAsync();

        // The correct token is the baseline that must succeed.
        var ok = await GetStatusAsync(anonymous, setup.TenantId, setup.OrderId, initiation.ResultToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // A wrong token is the reference generic-404 body every other miss must
        // match.
        var wrongToken = await GetStatusAsync(anonymous, setup.TenantId, setup.OrderId, "0".PadRight(64, '0'));
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        var expected = await CanonicalProblemBodyAsync(wrongToken);

        // A well-formed order id that does not exist.
        var missingOrder = await GetStatusAsync(anonymous, setup.TenantId, TsidId.Format(TsidId.NewId()), initiation.ResultToken);
        Assert.Equal(HttpStatusCode.NotFound, missingOrder.StatusCode);

        // The real order under another tenant.
        var crossTenant = await GetStatusAsync(anonymous, tenantB, setup.OrderId, initiation.ResultToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        // A token on an order that has no attempts at all.
        var bareSetup = await SetupPendingOrderAsync();
        var noAttempts = await GetStatusAsync(anonymous, bareSetup.TenantId, bareSetup.OrderId, initiation.ResultToken);
        Assert.Equal(HttpStatusCode.NotFound, noAttempts.StatusCode);

        // Every miss body is the same generic one — the caller cannot tell
        // which half (if any) was right.
        Assert.Equal(expected, await CanonicalProblemBodyAsync(missingOrder));
        Assert.Equal(expected, await CanonicalProblemBodyAsync(crossTenant));
        Assert.Equal(expected, await CanonicalProblemBodyAsync(noAttempts));

        // The order is untouched by all of the probes above.
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
        Assert.Equal(1, await CountAttemptsAsync(setup.OrderId, "Initiated"));
    }

    // ─── 12. Sandbox resolve exists and works in Development ───────────────────

    [Fact]
    public async Task SandboxResolve_InDevelopment_Returns200_ForApproveAndDecline()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiate = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var authority = ParseAuthorityFromRedirectUrl(initiate.RedirectUrl);

        // Decline first: the order stays payable, so a second initiation for the
        // same order is still possible (proving the route is live and honest).
        var decline = await ResolveSandboxAsync(anonymous, setup.TenantId, setup.OrderId, authority, approved: false);
        Assert.Equal(HttpStatusCode.OK, decline.StatusCode);
        Assert.Equal("PendingPayment", (await decline.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!.Status);

        var second = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var approve = await ResolveSandboxAsync(
            anonymous, setup.TenantId, setup.OrderId, ParseAuthorityFromRedirectUrl(second.RedirectUrl), approved: true);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal("Paid", (await approve.Content.ReadFromJsonAsync<B044LPaymentStatusResponse>())!.Status);
    }

    // ─── 13. Production + Sandbox fails closed at startup ──────────────────────

    [Fact]
    public void ProductionWithSandboxProvider_FailsClosedAtStartup()
    {
        using var factory = new ShopSandboxProductionApiFactory(db);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("Sandbox", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Development", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── 14. The migration applies cleanly on top of the existing history ──────

    [Fact]
    public async Task NewMigration_AppliesCleanly_OnExistingHistory_AndAddsExactlyTheNewSchema()
    {
        using var factory = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db);
        using var client = factory.CreateClient();

        // Starting this host ran MigrateAsync over the full Shop history — the
        // new migration applied on top of the existing one(s) with no conflict.
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // The new migration row is recorded in the Shop-owned history table.
        Assert.True(await MigrationHistoryHasRowAsync(db.ConnectionString, "AddShopPaymentLifecycle"));

        // The new table exists…
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = """
            select count(*) from information_schema.tables
            where table_schema = 'public' and table_name = 'shop_payment_initiations';
            """;
        Assert.Equal(1, Convert.ToInt32(await tableCommand.ExecuteScalarAsync()));

        // …and the attempt table carries exactly the six new columns.
        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText = """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'shop_payment_attempts'
              AND column_name IN ('amount_snapshot','callback_token_hash','failure_code',
                                  'provider_reference','verified_at_utc','version')
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await columnCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(
            // C-collation order: "verified_at_utc" ("verifi") sorts before
            // "version" ("versi").
            ["amount_snapshot", "callback_token_hash", "failure_code", "provider_reference", "verified_at_utc", "version"],
            columns);
    }

    // ─── Extra: the redirect URL is a relative same-origin bank path ───────────

    [Fact]
    public async Task Initiate_RedirectUrl_IsTheRelativeBankPath_WithTheAuthority()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var initiation = await InitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        var authority = ParseAuthorityFromRedirectUrl(initiation.RedirectUrl);

        // Exactly the B044 contract: a relative, same-origin path to the real
        // in-app bank route — never an absolute URL.
        Assert.Equal($"/shop/{setup.TenantId}/bank?authority={authority}", initiation.RedirectUrl);
        Assert.StartsWith("/shop/", initiation.RedirectUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("://", initiation.RedirectUrl, StringComparison.Ordinal);
    }

    // ─── Extra: Idempotency-Key and id validation ──────────────────────────────

    [Fact]
    public async Task Initiate_MissingOrMalformedIdempotencyKey_Returns400_NamingTheHeader()
    {
        var setup = await SetupPendingOrderAsync();
        using var anonymous = CreateClient();

        var missing = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, idempotencyKey: null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Contains("Idempotency-Key", missingBody, StringComparison.Ordinal);

        var malformed = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId, "not-a-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("Idempotency-Key", await malformed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Malformed / foreign / missing ids are the one generic 404, never a 500.
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, "not-a-tenant", setup.OrderId, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, setup.TenantId, "not-an-order", Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostInitiateAsync(anonymous, setup.TenantId, TsidId.Format(TsidId.NewId()), Guid.NewGuid().ToString())).StatusCode);

        // Nothing was persisted by any of the rejected requests.
        Assert.Equal(0, await CountAttemptsAsync(setup.OrderId));
        Assert.Equal("PendingPayment", await GetOrderStatusAsync(setup.OrderId));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> NewTenantForIsolationAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        return await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
    }

    private static async Task<bool> MigrationHistoryHasRowAsync(string connectionString, string migrationNameSuffix)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select count(*) from "__ShopMigrationsHistory"
            where "MigrationId" like @suffix;
            """;
        command.Parameters.Add(new NpgsqlParameter("@suffix", $"%{migrationNameSuffix}"));
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) >= 1;
    }
}

/// <summary>
/// A Production host deliberately configured with
/// <c>Shop:Payments:Provider=Sandbox</c>, used only to prove that B044's
/// fail-closed rule refuses to start the application rather than silently
/// allowing the browser-driven payment simulation outside Development.
/// </summary>
internal sealed class ShopSandboxProductionApiFactory(IamDbFixtureBase db)
    : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-shop-lifecycle-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Production");

        var values = new Dictionary<string, string?>
        {
            ["IAM:IamDb"] = db.ConnectionString,
            ["IAM:Auth:SigningKey"] = "some-signing-key-long-enough-32bytes!!",
            ["Shop:ShopDb"] = db.ConnectionString,
            ["Shop:MediaRoot"] = Path.Combine(_contentRoot, "shop-media"),
            ["Shop:CartReservationMinutes"] = "30",
            ["Shop:CartCleanupIntervalSeconds"] = "3600",
            // The exact value that must be refused outside Development.
            ["Shop:Payments:Provider"] = "Sandbox"
        };

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));
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
