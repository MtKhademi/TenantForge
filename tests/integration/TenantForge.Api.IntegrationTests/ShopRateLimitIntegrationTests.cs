using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Shop.Features.RateLimiting;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B046's public-abuse controls on the anonymous Shop surface: the
/// four named per-policy rate limits (order lookup, cart mutation,
/// checkout/order creation, payment initiation), the tenant+effective-IP
/// partitioning, the one generic 429, the trusted-proxy rule for forwarded
/// headers, the request-body / callback-query size bounds (413), the
/// secret-free rejection log line, and the Production fail-closed startup.
///
/// No new routes exist — the limiter only wraps routes that already exist and
/// already carry <c>RequireRateLimiting</c>. Every fact drives the REAL routes
/// with a bare, anonymous client (no Authorization header) against a real
/// PostgreSQL fixture, and reads the persisted side effects back through raw
/// SQL where a status-code-only assertion would be insufficient.
///
/// The class runs on a dedicated database (ShopRateLimitIsolatedCollection) so
/// its tenants, carts, orders and payment attempts never inflate the other
/// Shop databases' row counts. Each xunit fact gets its own class instance and
/// therefore its own <see cref="_factory"/> (a fresh in-memory limiter) while
/// the database is shared across the collection.
/// </summary>
[Collection(nameof(ShopRateLimitIsolatedCollection))]
public sealed class ShopRateLimitIntegrationTests(ShopRateLimitDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The per-minute limit every policy is configured to in this class —
    /// deliberately tiny so a fact sends at most a handful of requests. Each
    /// fact fills the limit (admitted) and asserts the next request is a 429.
    /// </summary>
    private const int Limit = 2;

    private readonly ShopRateLimitApiFactory _factory = new(db, perMinute: Limit, maxBodyBytes: 4096);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    // ─── Wire DTOs (B046L-prefixed: the test assembly is one namespace) ───────

    private sealed record B046LVariant(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

    private sealed record B046LProductDetail(string Id, string Name, string Slug, IReadOnlyList<B046LVariant> Variants);

    private sealed record B046LOrderCreated(string OrderId, string OrderNumber, string TrackingCode, string Status);

    private sealed record B046LInitiate(string Provider, string RedirectUrl, string ResultToken);

    private sealed record SetupOrder(string TenantId, string OrderId, string TrackingCode, string Phone);

    private sealed record SetupCartOnly(string TenantId, string CartId);

    // ─── Setup helpers (mirroring the established Shop test fixtures) ─────────

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

    private async Task<(string TenantId, HttpClient Member)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var member = CreateClient();
        SetMemberToken(member, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, member);
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

    private async Task<string> CreateSingleVariantAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "B046", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // The variant id is read back through the anonymous product detail so
        // this test never reaches into the module's internal entity types.
        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B046LProductDetail>())!;
        return detail.Variants[0].Id;
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

    /// <summary>
    /// Authors a tenant, a single-variant product, a Tehran rate, a cart, an
    /// item and a real PendingPayment order — the state the lookup and payment
    /// facts start from. This consumes the setup tenant's cart (2) and
    /// checkout/order (1) budgets, but NOT the lookup or payment budgets, so
    /// those facts fill their own policies cleanly.
    /// </summary>
    private async Task<SetupOrder> SetupOrderAsync(decimal unitPrice = 100000m)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var productSlug = $"shirt-{suffix}";
        var variantId = await CreateSingleVariantAsync(member, tenantId, categoryId, "Shirt", productSlug, stockQuantity: 10, unitPrice);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", cost: 0m)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantId, quantity: 1)).StatusCode);

        var response = await PostOrderAsync(anonymous, tenantId, cartId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<B046LOrderCreated>())!;
        Assert.Equal("PendingPayment", order.Status);
        return new SetupOrder(tenantId, order.OrderId, order.TrackingCode, "09121234567");
    }

    /// <summary>
    /// Authors a tenant, a single-variant product, a Tehran rate, a cart and an
    /// item — but deliberately creates NO order, so the checkout/order policy's
    /// budget stays untouched for the fact that then fills it via
    /// checkout-summary calls.
    /// </summary>
    private async Task<SetupCartOnly> SetupCartOnlyAsync(decimal unitPrice = 100000m)
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var productSlug = $"shirt-{suffix}";
        var variantId = await CreateSingleVariantAsync(member, tenantId, categoryId, "Shirt", productSlug, stockQuantity: 10, unitPrice);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", cost: 0m)).StatusCode);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantId, quantity: 1)).StatusCode);
        return new SetupCartOnly(tenantId, cartId);
    }

    // ─── The endpoints under test ─────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostLookupAsync(
        HttpClient anonymous, string tenantId, string? trackingCode, string? customerPhone)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders/lookup", new { trackingCode, customerPhone });

    private static Task<HttpResponseMessage> PostCheckoutSummaryAsync(
        HttpClient anonymous, string tenantId, string cartId)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/checkout/summary", new
        {
            cartId,
            shippingProvince = "Tehran",
            shippingCity = (string?)null,
            couponCode = (string?)null
        });

    private static async Task<HttpResponseMessage> PostInitiateAsync(
        HttpClient anonymous, string tenantId, string orderId, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/shop/{tenantId}/orders/{orderId}/payments/initiate");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await anonymous.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CreateCartRawAsync(HttpClient anonymous, string tenantId)
        => await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);

    private static Task<HttpResponseMessage> PostLookupWithForwardedForAsync(
        HttpClient anonymous, string tenantId, string trackingCode, string phone, string forwardedFor)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/shop/{tenantId}/orders/lookup");
        request.Content = JsonContent.Create(new { trackingCode, customerPhone = phone });
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return anonymous.SendAsync(request);
    }

    // ─── 429 problem assertions ───────────────────────────────────────────────

    /// <summary>
    /// The exact RFC 7807 429 every Shop over-limit request must carry: the
    /// shared <c>shop_rate_limit</c> type, the one generic detail, an integer
    /// <c>Retry-After</c> of one second (header) and the matching integer
    /// <c>retryAfter</c> (body). It names no tenant, cart, order, phone,
    /// tracking code, coupon or authority.
    /// </summary>
    private static async Task<JsonNode> AssertShopRateLimit429Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta is not null,
            "the 429 must carry an integer Retry-After header");
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var node = (JsonNode?)JsonSerializer.Deserialize<JsonNode>(await response.Content.ReadAsStringAsync(), Json)!;
        Assert.Equal("shop_rate_limit", (string)node!["type"]!);
        Assert.Equal("Too many requests", (string)node["title"]!);
        Assert.Equal("Too many requests. Please try again later.", (string)node["detail"]!);
        Assert.Equal(429, (int)node["status"]!);
        Assert.Equal(1, (int)node["retryAfter"]!);
        return node;
    }

    /// <summary>
    /// Canonicalizes a 429 body so two rejections can be compared for exact
    /// equality (the hand-written body carries no traceId, so nothing needs
    /// stripping — the point is that nothing may differ at all).
    /// </summary>
    private static async Task<string> Canonical429BodyAsync(HttpResponseMessage response)
        => (await response.Content.ReadAsStringAsync());

    // ─── 1a. OrderLookup policy: limit admitted, then 429 ─────────────────────

    [Fact]
    public async Task OrderLookupPolicy_LimitAdmitted_ThenNextIs429()
    {
        var setup = await SetupOrderAsync();
        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization); // route is anonymous

        for (var i = 0; i < Limit; i++)
        {
            var admitted = await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode); // admitted AND a real success
        }

        var rejected = await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone);
        await AssertShopRateLimit429Async(rejected);
    }

    // ─── 1b. CartMutation policy: limit admitted, then 429 ────────────────────

    [Fact]
    public async Task CartMutationPolicy_LimitAdmitted_ThenNextIs429()
    {
        // A fresh, empty tenant: cart creation needs no product, and a clean
        // tenant guarantees the setup did not spend any of this bucket's budget.
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            var admitted = await CreateCartRawAsync(anonymous, tenantId);
            Assert.Equal(HttpStatusCode.Created, admitted.StatusCode); // genuinely successful
        }

        var rejected = await CreateCartRawAsync(anonymous, tenantId);
        await AssertShopRateLimit429Async(rejected);
    }

    // ─── 1c. CheckoutOrder policy: limit admitted, then 429 ───────────────────

    [Fact]
    public async Task CheckoutOrderPolicy_LimitAdmitted_ThenNextIs429()
    {
        var setup = await SetupCartOnlyAsync(); // no order created → policy budget untouched
        using var anonymous = CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            var admitted = await PostCheckoutSummaryAsync(anonymous, setup.TenantId, setup.CartId);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode); // admitted AND a real success
        }

        var rejected = await PostCheckoutSummaryAsync(anonymous, setup.TenantId, setup.CartId);
        await AssertShopRateLimit429Async(rejected);
    }

    // ─── 1d. Payment policy: limit admitted, then 429 ─────────────────────────

    [Fact]
    public async Task PaymentPolicy_LimitAdmitted_ThenNextIs429()
    {
        var setup = await SetupOrderAsync();
        using var anonymous = CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            var admitted = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode); // admitted AND a real success
        }

        var rejected = await PostInitiateAsync(anonymous, setup.TenantId, setup.OrderId);
        await AssertShopRateLimit429Async(rejected);
    }

    // ─── 2. Partition isolation: one tenant+IP bucket does not drain another ──

    [Fact]
    public async Task TwoTenantPartitions_SameIp_ExhaustingOneDoesNotAffectTheOther()
    {
        var (tenantA, _) = await NewTenantWithOwnerAsync();
        var (tenantB, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        // Interleave the two tenants (same effective IP, different tenant) so
        // the traffic is as close to concurrent as a single client allows.
        Assert.Equal(HttpStatusCode.Created, (await CreateCartRawAsync(anonymous, tenantA)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateCartRawAsync(anonymous, tenantB)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateCartRawAsync(anonymous, tenantA)).StatusCode); // A now at the limit

        // A is now exhausted: the next cart creation for A is rejected…
        var aRejected = await CreateCartRawAsync(anonymous, tenantA);
        await AssertShopRateLimit429Async(aRejected);

        // …but B still has its full remaining quota — A's 429 did not touch B's
        // bucket (the partition key is tenant|ip, and the tenants differ).
        Assert.Equal(HttpStatusCode.Created, (await CreateCartRawAsync(anonymous, tenantB)).StatusCode);
    }

    // ─── 3. Identical 429 for a valid and an invalid lookup ───────────────────

    [Fact]
    public async Task ValidLookup429_AndInvalidLookup429_HaveIdenticalRetryAfterAndBody()
    {
        var setup = await SetupOrderAsync();          // tenant A: a real, existing order
        var (tenantB, _) = await NewTenantWithOwnerAsync(); // tenant B: no order at all

        using var anonymous = CreateClient();

        // Fill tenant A's lookup budget with the VALID pair, then the 429.
        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone)).StatusCode);
        }
        var valid429 = await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone);

        // Fill tenant B's lookup budget with a FABRICATED code (a miss), then the 429.
        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PostLookupAsync(anonymous, tenantB, "ZZZZZZZZZZZZ", "09000000000")).StatusCode);
        }
        var invalid429 = await PostLookupAsync(anonymous, tenantB, "ZZZZZZZZZZZZ", "09000000000");

        // The two 429s are indistinguishable: identical Retry-After and body. A
        // caller cannot tell that one of them "really" existed.
        await AssertShopRateLimit429Async(valid429);
        await AssertShopRateLimit429Async(invalid429);
        Assert.Equal(
            await Canonical429BodyAsync(valid429),
            await Canonical429BodyAsync(invalid429));
        Assert.Equal(
            valid429.Headers.RetryAfter?.Delta,
            invalid429.Headers.RetryAfter?.Delta);
    }

    // ─── 4. Untrusted forwarded-for is ignored; the direct IP is used ─────────

    [Fact]
    public async Task ForwardedFor_FromUntrustedSource_IsIgnored_AndDirectIpIsUsed()
    {
        // The factory configures NO trusted proxy, so UseForwardedHeaders must
        // ignore X-Forwarded-For and partition by the direct (loopback) remote
        // IP. Proof: two DIFFERENT client-claimed IPs collapse into the SAME
        // bucket — so filling the budget under one claimed IP rejects the next
        // request under a different claimed IP.
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            var admitted = await PostLookupWithForwardedForAsync(anonymous, tenantId, "AAAAAAAAAAAA", "09121234567", "10.0.0.1");
            Assert.Equal(HttpStatusCode.NotFound, admitted.StatusCode); // admitted (a miss), not 429
        }

        // A different claimed source IP. If it were honored it would be a fresh
        // bucket and admitted; because it is ignored it shares the exhausted
        // direct-IP bucket and is rejected.
        var rejected = await PostLookupWithForwardedForAsync(anonymous, tenantId, "AAAAAAAAAAAA", "09121234567", "99.99.99.99");
        await AssertShopRateLimit429Async(rejected);
    }

    // ─── 5. Production with a missing rate-limit value fails closed ───────────

    [Fact]
    public void ProductionWithMissingRateLimitConfig_FailsClosedAtStartup()
    {
        using var factory = new ShopRateLimitProductionApiFactory(db);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        // The host refuses to start because a per-minute value is missing in
        // Production — it does not fall back to a default and run unprotected.
        Assert.Contains("RateLimiting", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
    }

    // ─── 6. The rejection log names policy + tenant, never the secrets ────────

    [Fact]
    public async Task RateLimitRejection_LogsPolicyAndTenantId_NeverPhoneTrackingCouponOrAuthority()
    {
        var setup = await SetupOrderAsync();
        using var anonymous = CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone)).StatusCode);
        }

        // Isolate the rejection's own log line: clear, then fire exactly the
        // request that is rejected (it never reaches the handler, so the only
        // thing that can log it is the rate-limit middleware).
        _factory.LogCollector.Clear();
        var rejected = await PostLookupAsync(anonymous, setup.TenantId, setup.TrackingCode, setup.Phone);
        await AssertShopRateLimit429Async(rejected);

        var entries = _factory.LogCollector.Entries;
        var rejectionLog = Assert.Single(entries, e => e.Contains("shop-order-lookup", StringComparison.Ordinal));
        // The log names the tenant id the operator can use to correlate…
        Assert.Contains(setup.TenantId, rejectionLog, StringComparison.Ordinal);
        // …but never the values a caller typed or that the gateway would carry.
        foreach (var secret in new[] { setup.Phone, setup.TrackingCode })
        {
            Assert.DoesNotContain(secret, rejectionLog, StringComparison.Ordinal);
        }
        foreach (var entry in entries)
        {
            Assert.DoesNotContain(setup.Phone, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(setup.TrackingCode, entry, StringComparison.Ordinal);
        }
    }

    // ─── 7. Normal (non-abuse) traffic is not globally throttled ──────────────

    [Fact]
    public async Task NormalCustomerJourney_UnderTheLimits_IsNotThrottled()
    {
        // A factory at the documented Development defaults: a realistic
        // customer journey stays far under every limit, so nothing may 429.
        using var factory = new ShopRateLimitApiFactory(db, perMinute: 30, maxBodyBytes: 512 * 1024);
        var admin = await PlatformAdminClientAsync(factory);
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var member = factory.CreateClient();
        SetMemberToken(member, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");

        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Apparel", $"cat-{suffix}");
        var variantId = await CreateSingleVariantAsyncFactory(factory, member, tenantId, categoryId, "Shirt", $"shirt-{suffix}", 10, 100000m);
        Assert.Equal(HttpStatusCode.OK, (await SetShippingRateAsync(member, tenantId, "Tehran", 0m)).StatusCode);

        using var anonymous = factory.CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantId, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostCheckoutSummaryAsync(anonymous, tenantId, cartId)).StatusCode);
        var orderResponse = await PostOrderAsync(anonymous, tenantId, cartId);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<B046LOrderCreated>())!;
        Assert.Equal("PendingPayment", order.Status);
        Assert.Equal(HttpStatusCode.OK, (await PostInitiateAsync(anonymous, tenantId, order.OrderId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLookupAsync(anonymous, tenantId, order.TrackingCode, "09121234567")).StatusCode);

        // A public catalog read is not rate-limited at all in this task: a
        // short burst of reads still all succeeds (nothing was globally
        // throttled by the new limiter).
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/shop/{tenantId}/products/shirt-{suffix}")).StatusCode);
        }

        // The order was actually created (a status-code-only run would not prove
        // the journey completed).
        Assert.Equal(1, await CountOrdersAsync(tenantId));
    }

    private async Task<HttpClient> PlatformAdminClientAsync(ShopRateLimitApiFactory factory)
    {
        var client = factory.CreateClient();
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

    private async Task<string> CreateSingleVariantAsyncFactory(
        ShopRateLimitApiFactory factory, HttpClient memberClient, string tenantId, string categoryId,
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
            variants = new object[] { new { color = "Black", size = "M", sku = "B046", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var anonymous = factory.CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B046LProductDetail>())!;
        return detail.Variants[0].Id;
    }

    // ─── 8. Oversized JSON body is rejected with a generic 413 ────────────────

    [Fact]
    public async Task OversizedJsonShopBody_IsRejected_WithGeneric413_BeforeAnyWork()
    {
        using var anonymous = CreateClient();
        // Any well-formed tenant: the guard is path + content-length + type,
        // and runs before routing resolves the tenant, so no real tenant is
        // needed (and none is leaked into the response).
        var tenantId = TsidId.Format(TsidId.NewId());

        var payload = new { data = new string('x', 5000) }; // > the 4096-byte bound
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await anonymous.PostAsync($"/api/shop/{tenantId}/orders", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("shop_request_too_large", body, StringComparison.Ordinal);
        Assert.DoesNotContain(tenantId, body, StringComparison.Ordinal);
    }

    // ─── 9. Oversized ZarinPal callback query is rejected with a generic 413 ──

    [Fact]
    public async Task OversizedZarinPalCallbackQuery_IsRejected_WithGeneric413()
    {
        using var anonymous = CreateClient();
        var tenantId = TsidId.Format(TsidId.NewId());
        var bigState = new string('a', ShopRateLimitOptions.MaxCallbackQueryLength + 1); // > 16,000

        var response = await anonymous.GetAsync(
            $"/api/shop/{tenantId}/payments/zarinpal/callback?state={Uri.EscapeDataString(bigState)}");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("shop_callback_too_large", body, StringComparison.Ordinal);
        Assert.DoesNotContain(bigState, body, StringComparison.Ordinal);
    }

    // ─── Raw-SQL persistence reader ───────────────────────────────────────────

    private async Task<int> CountOrdersAsync(string tenantId)
    {
        Assert.True(TsidId.TryParse(tenantId, out var tenantTsid), "tenantId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from shop_orders where tenant_id = @p;";
        command.Parameters.Add(new NpgsqlParameter("@p", tenantTsid.ToLong()));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// A Development host with the per-policy limits and request-body bound
    /// configured explicitly (tiny, so each fact sends only a handful of
    /// requests). No trusted proxy is configured, so forwarded headers are
    /// never honored — which is exactly what the untrusted-forwarded-for fact
    /// relies on.
    /// </summary>
    private sealed class ShopRateLimitApiFactory(IamDbFixtureBase db, int perMinute, long maxBodyBytes)
        : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _contentRoot =
            Path.Combine(Path.GetTempPath(), "tenantforge-shop-rate-limit-tests", Guid.NewGuid().ToString("N"));

        public TestLogCollector LogCollector { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_contentRoot);
            builder.UseContentRoot(_contentRoot);
            builder.UseEnvironment("Development");

            var values = new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Debug",
                ["AllowedOrigins:0"] = "http://localhost:5173",
                ["IAM:IamDb"] = db.ConnectionString,
                ["IAM:Auth:SigningKey"] = ApiFactory.SigningKey,
                ["IAM:SeedAdmin:Email"] = ApiFactory.Email,
                ["IAM:SeedAdmin:Password"] = ApiFactory.Password,
                ["IAM:SeedAdmin:DisplayName"] = ApiFactory.DisplayName,
                ["Shop:ShopDb"] = db.ConnectionString,
                ["Shop:MediaRoot"] = Path.Combine(_contentRoot, "shop-media"),
                ["Shop:CartReservationMinutes"] = "30",
                ["Shop:CartCleanupIntervalSeconds"] = "3600",
                ["Shop:Payments:Provider"] = "Sandbox",
                // B046: explicit, tiny limits so each fact needs only a few
                // requests to fill a policy and observe the 429.
                ["Shop:RateLimiting:OrderLookupPerMinute"] = perMinute.ToString(),
                ["Shop:RateLimiting:CartMutationPerMinute"] = perMinute.ToString(),
                ["Shop:RateLimiting:CheckoutOrderPerMinute"] = perMinute.ToString(),
                ["Shop:RateLimiting:PaymentInitiationPerMinute"] = perMinute.ToString(),
                ["Shop:RateLimiting:MaxRequestBodyBytes"] = maxBodyBytes.ToString(),
            };

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(values));

            builder.ConfigureLogging((_, logging) =>
                logging.AddProvider(new CollectorLoggerProvider(LogCollector)));
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

    /// <summary>
    /// A Production host that is complete and valid in every respect EXCEPT
    /// the <c>Shop:RateLimiting</c> section, which is deliberately absent —
    /// the only thing allowed to make startup fail. The ZarinPal section is the
    /// full, valid Production shape so the provider check passes and the
    /// failure is provably the rate-limit configuration, not the payments one.
    /// </summary>
    private sealed class ShopRateLimitProductionApiFactory(IamDbFixtureBase db)
        : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _contentRoot =
            Path.Combine(Path.GetTempPath(), "tenantforge-shop-rate-limit-production-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_contentRoot);
            builder.UseContentRoot(_contentRoot);
            builder.UseEnvironment("Production");

            var values = new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Debug",
                ["IAM:IamDb"] = db.ConnectionString,
                ["IAM:Auth:SigningKey"] = "production-signing-key-do-not-use-32b!!",
                ["Shop:ShopDb"] = db.ConnectionString,
                ["Shop:MediaRoot"] = Path.Combine(_contentRoot, "shop-media"),
                ["Shop:CartReservationMinutes"] = "30",
                ["Shop:CartCleanupIntervalSeconds"] = "3600",
                // Valid Production payments configuration, so ValidatePaymentsProvider
                // passes and the failure is provably the missing rate-limit section.
                ["Shop:Payments:Provider"] = "ZarinPal",
                ["Shop:Payments:ZarinPal:MerchantId"] = "test-merchant-id",
                ["Shop:Payments:ZarinPal:Currency"] = "IRT",
                ["Shop:Payments:ZarinPal:RequestEndpoint"] = "https://dev.zarinpal.com/v4/payment/request",
                ["Shop:Payments:ZarinPal:VerifyEndpoint"] = "https://dev.zarinpal.com/v4/payment/verify",
                ["Shop:Payments:ZarinPal:GatewayBaseUrl"] = "https://dev.zarinpal.com/payment",
                ["Shop:Payments:ZarinPal:PublicApiBaseUrl"] = "https://api.tenantforge.local",
                ["Shop:Payments:ZarinPal:FrontendResultBaseUrl"] = "https://frontend.tenantforge.local",
                ["Shop:Payments:ZarinPal:TimeoutSeconds"] = "10",
                // NOTE: no Shop:RateLimiting:* value — that is the deliberate gap.
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
}

