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
/// Protects B043's operator order-status mutations on
/// <c>PATCH …/orders/{orderId}/status</c>: the exact transition table
/// (Paid→Fulfilled, PendingPayment→Cancelled, nothing else), optimistic
/// concurrency via <c>expectedVersion</c>, idempotency-key replay and
/// same-key/different-action conflicts, the exactly-once inventory restore on
/// cancel (proven with two concurrent cancels), and the
/// <c>Shop.Orders.Manage</c> permission matrix B042 reserved.
///
/// Assertions read the real PostgreSQL rows back through raw SQL (status,
/// version, the three new timestamps, variant stock, attempt status, and the
/// operation row) — a status-code-only test would not be sufficient.
///
/// Runs on a dedicated database (ShopOrderOperationsIsolatedCollection) so its
/// tenants, products, orders and stock never inflate the other Shop databases.
/// </summary>
[Collection(nameof(ShopOrderOperationsIsolatedCollection))]
public sealed class ShopOrderOperationsIntegrationTests(ShopOrderOperationsDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    // ─── Response / wire DTOs (B043-prefixed: the assembly is one namespace) ──

    internal sealed record B043AdminOrderCustomerDto(
        string Name, string Phone, string ShippingProvince, string ShippingCity,
        string ShippingAddressLine, string ShippingPostalCode);

    internal sealed record B043AdminOrderTotalsDto(
        decimal SubTotal, decimal ShippingCost, decimal DiscountAmount, decimal GrandTotal);

    internal sealed record B043OrderLookupItemDto(
        string ProductNameSnapshot, string VariantLabelSnapshot, decimal UnitPrice, int Quantity);

    internal sealed record B043AdminPaymentAttemptDto(string Id, string Status, DateTimeOffset CreatedAtUtc);

    internal sealed record B043AdminOrderDetailDto(
        string Id, string OrderNumber, string TrackingCode, string Status,
        B043AdminOrderCustomerDto Customer, B043AdminOrderTotalsDto Totals,
        IReadOnlyList<B043OrderLookupItemDto> Items,
        IReadOnlyList<B043AdminPaymentAttemptDto> PaymentAttempts,
        int Version, DateTimeOffset CreatedAtUtc);

    internal sealed record B043ProblemDto(string Type, string Title, string? Detail);

    internal sealed record B043ValidationProblemDto(IReadOnlyDictionary<string, string[]> Errors);

    internal sealed record B043StorefrontVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

    internal sealed record B043ProductDetailDto(
        string Id, string CategoryId, string Name, string Slug, string Description,
        decimal BasePrice, decimal? CompareAtPrice,
        IReadOnlyList<B043StorefrontVariantDto> Variants);

    internal sealed record B043OrderCreatedDto(
        string OrderId, string OrderNumber, string TrackingCode, string Status,
        decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

    // B044: initiation now carries an Idempotency-Key header and returns
    // { provider, redirectUrl, resultToken }; the authority the sandbox page
    // hands back is embedded in the redirectUrl, so the helper parses it out
    // (exactly what the fake bank page does).
    internal sealed record B043InitiatePaymentDto(string Provider, string RedirectUrl, string ResultToken);

    internal sealed record B043PaymentStatusDto(string OrderNumber, string Status, string? ProviderReference);

    // ─── Setup helpers (mirror B042's admin-order test helpers) ────────────────

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

    private async Task<(string TenantId, HttpClient OwnerClient, Tsid OwnerAccount, Tsid MemberAccount)> NewTenantAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var ownerClient = CreateClient();
        SetMemberToken(ownerClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");

        var tenantTsid = TsidId.TryParseNullable(tenantId);
        if (tenantTsid is null)
        {
            throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        }

        await using var context = db.CreateContext();
        var member = Account.CreateUser($"member-{Guid.NewGuid():N}@tenantforge.local", "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(member);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid.Value, member.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        return (tenantId, ownerClient, ownerAccount, member.Id);
    }

    private async Task<Tsid> AddMemberAsync(string tenantId)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId);
        if (tenantTsid is null)
        {
            throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        }

        await using var context = db.CreateContext();
        var member = Account.CreateUser($"member-{Guid.NewGuid():N}@tenantforge.local", "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(member);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid.Value, member.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return member.Id;
    }

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

    private async Task<(string ProductId, string VariantId, int InitialStock)> CreateSingleVariantAsync(
        HttpClient memberClient, string tenantId, string name, string slug, int stockQuantity, decimal basePrice)
    {
        var categoryResponse = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Shirts",
            slug = $"cat-{Guid.NewGuid():N}"[..16],
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        using var categoryDocument = JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
        var categoryId = categoryDocument.RootElement.GetProperty("id").GetString()!;

        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "Black", size = "M", sku = "OPS", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var productDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productId = productDocument.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B043ProductDetailDto>())!;
        Assert.Single(detail.Variants);
        return (productId, detail.Variants[0].Id, stockQuantity);
    }

    private static Task<HttpResponseMessage> SetShippingRateAsync(HttpClient client, string tenantId, decimal cost)
        => client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost });

    private async Task<string> CreateOrderAsync(
        string tenantId, HttpClient adminClient, string variantId, string customerName, string customerPhone, int quantity = 1)
    {
        var rateResponse = await SetShippingRateAsync(adminClient, tenantId, 0m);
        Assert.Equal(HttpStatusCode.OK, rateResponse.StatusCode);

        using var anonymous = CreateClient();
        var cartResponse = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, cartResponse.StatusCode);
        var cartId = (await cartResponse.Content.ReadFromJsonAsync<B043CartCreatedDto>())!.CartId;

        var addResponse = await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/carts/{cartId}/items",
            new { productVariantId = variantId, quantity });
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var orderResponse = await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName,
            customerPhone,
            shippingProvince = "Tehran",
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890"
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var created = (await orderResponse.Content.ReadFromJsonAsync<B043OrderCreatedDto>())!;
        return created.OrderId;
    }

    internal sealed record B043CartCreatedDto(string CartId, string? ExpiresAtUtc);

    // B044: initiation now requires an Idempotency-Key header and returns
    // { provider, redirectUrl, resultToken }. The sandbox redirectUrl is
    // /shop/{tenantId}/bank?authority={authority}, so the helper parses the
    // authority out — the same value the fake bank page would hand back.
    private static async Task<string> InitiatePaymentAsync(HttpClient anonymous, string tenantId, string orderId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/shop/{tenantId}/orders/{orderId}/payments/initiate");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await anonymous.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var initiation = (await response.Content.ReadFromJsonAsync<B043InitiatePaymentDto>())!;
        return ParseAuthorityFromRedirectUrl(initiation.RedirectUrl);
    }

    // B044: the browser-driven simulation moved to the Development-only
    // sandbox resolve route (the old general callback route is deleted).
    private static Task<HttpResponseMessage> ResolveSandboxAsync(
        HttpClient anonymous, string tenantId, string orderId, string authority, bool approved)
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders/{orderId}/payments/sandbox/resolve",
            new { authority, approved });

    private static string ParseAuthorityFromRedirectUrl(string redirectUrl)
    {
        // /shop/{tenantId}/bank?authority={authority}
        var index = redirectUrl.IndexOf("authority=", StringComparison.Ordinal);
        Assert.True(index >= 0, $"redirectUrl must carry an authority: {redirectUrl}");
        return redirectUrl[(index + "authority=".Length)..];
    }

    // ─── The endpoint under test ─────────────────────────────────────────────

    private static Task<HttpResponseMessage> ChangeStatusAsync(
        HttpClient client, string tenantId, string orderId, string action, int expectedVersion, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/tenants/{tenantId}/shop/orders/{orderId}/status")
        {
            Content = JsonContent.Create(new { action, expectedVersion })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ChangeStatusRawAsync(
        HttpClient client, string tenantId, string orderId, string bodyJson, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/tenants/{tenantId}/shop/orders/{orderId}/status")
        {
            Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    // ─── Raw-SQL persistence readers ─────────────────────────────────────────

    private async Task<(string Status, int Version, string? FulfilledAtUtc, string? CancelledAtUtc, string? InventoryReleasedAtUtc)>
        ReadOrderRowAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, version,
                   fulfilled_at_utc::text, cancelled_at_utc::text, inventory_released_at_utc::text
            FROM shop_orders WHERE id = @orderId;
            """;
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "order row must exist");
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private async Task<int> ReadVariantStockAsync(string variantId)
    {
        var variantTsid = RequireTsid(variantId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT stock_quantity FROM shop_product_variants WHERE id = @variantId;";
        command.Parameters.AddWithValue("variantId", variantTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task<int> CountInitiatedAttemptsAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_payment_attempts WHERE order_id = @orderId AND status = 'Initiated';";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task<int> CountFailedAttemptsAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_payment_attempts WHERE order_id = @orderId AND status = 'Failed';";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task<int> CountInvalidatedAttemptsAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_payment_attempts WHERE order_id = @orderId AND status = 'Invalidated';";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task<int> CountSucceededAttemptsAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_payment_attempts WHERE order_id = @orderId AND status = 'Succeeded';";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private async Task<int> CountOperationRowsAsync(string orderId)
    {
        var orderTsid = RequireTsid(orderId);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_order_operations WHERE order_id = @orderId;";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    private static Tsid RequireTsid(string value)
    {
        if (!TsidId.TryParse(value, out var tsid))
        {
            throw new InvalidOperationException($"{value} must be a canonical TSID string.");
        }

        return tsid;
    }

    // ─── 1. Fulfil a Paid order ───────────────────────────────────────────────

    [Fact]
    public async Task Fulfil_PaidOrder_SetsFulfilled_StampAndVersion_StockUntouched()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Fulfil Shirt", $"ful-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Fulfil Customer", "09121000001");

        // Reserve is already in effect from order creation: stock = initial - qty(1).
        var reservedStock = initialStock - 1;
        Assert.Equal(reservedStock, await ReadVariantStockAsync(variantId));

        // Pay the order (B044: via the sandbox resolve route) so it is in the
        // only status Fulfil may move from.
        using var anonymous = CreateClient();
        var authority = await InitiatePaymentAsync(anonymous, tenantId, orderId);
        Assert.Equal(HttpStatusCode.OK, (await ResolveSandboxAsync(anonymous, tenantId, orderId, authority, approved: true)).StatusCode);
        Assert.Equal("Paid", (await ReadOrderRowAsync(orderId)).Status);

        var key = Guid.NewGuid().ToString();
        var response = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Fulfill", 1, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<B043AdminOrderDetailDto>())!;
        Assert.Equal("Fulfilled", body.Status);
        Assert.Equal(2, body.Version);

        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("Fulfilled", row.Status);
        Assert.Equal(2, row.Version);
        Assert.NotNull(row.FulfilledAtUtc);
        Assert.Null(row.CancelledAtUtc);
        Assert.Null(row.InventoryReleasedAtUtc);

        // Fulfil must never touch stock.
        Assert.Equal(reservedStock, await ReadVariantStockAsync(variantId));
        // Exactly one operation row was recorded.
        Assert.Equal(1, await CountOperationRowsAsync(orderId));
    }

    // ─── 2. Cancel a PendingPayment order ─────────────────────────────────────

    [Fact]
    public async Task Cancel_PendingPaymentOrder_RestoresStock_AndInvalidatesInitiatedAttempts()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Cancel Shirt", $"can-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Cancel Customer", "09121000002");

        // Leave one Initiated payment attempt on the order (not resolved).
        using var anonymous = CreateClient();
        _ = await InitiatePaymentAsync(anonymous, tenantId, orderId);
        Assert.Equal(1, await CountInitiatedAttemptsAsync(orderId));

        var reservedStock = initialStock - 1;
        Assert.Equal(reservedStock, await ReadVariantStockAsync(variantId));

        var key = Guid.NewGuid().ToString();
        var response = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<B043AdminOrderDetailDto>())!;
        Assert.Equal("Cancelled", body.Status);
        Assert.Equal(2, body.Version);

        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("Cancelled", row.Status);
        Assert.Equal(2, row.Version);
        Assert.NotNull(row.CancelledAtUtc);
        Assert.Null(row.FulfilledAtUtc);
        Assert.NotNull(row.InventoryReleasedAtUtc);

        // Stock restored exactly once: back to the full initial quantity.
        Assert.Equal(initialStock, await ReadVariantStockAsync(variantId));
        // B044: the Initiated attempt is now Invalidated (not Failed — a
        // cancel is not a failed payment); no Initiated remains and no Failed
        // row was created by the cancel either.
        Assert.Equal(0, await CountInitiatedAttemptsAsync(orderId));
        Assert.Equal(0, await CountFailedAttemptsAsync(orderId));
        Assert.Equal(1, await CountInvalidatedAttemptsAsync(orderId));
        Assert.Equal(1, await CountOperationRowsAsync(orderId));
    }

    // ─── 3. Late verification for a Cancelled order never marks it Paid ───────

    [Fact]
    public async Task LatePaymentVerification_CancelledOrder_ReturnsCancelledOutcome_NeverPaid()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, _) = await CreateSingleVariantAsync(ownerClient, tenantId, "Late Shirt", $"late-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Late Customer", "09121000003");

        using var anonymous = CreateClient();
        var authority = await InitiatePaymentAsync(anonymous, tenantId, orderId);

        // Cancel first — this invalidates the Initiated attempt (B044).
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key)).StatusCode);
        Assert.Equal("Cancelled", (await ReadOrderRowAsync(orderId)).Status);
        Assert.Equal(0, await CountInitiatedAttemptsAsync(orderId));
        Assert.Equal(1, await CountInvalidatedAttemptsAsync(orderId));

        // A late success arriving for the (now invalidated) attempt returns the
        // already-computed outcome — the order's current Cancelled status — and
        // never moves it to Paid. The attempt is not re-resolved to Succeeded.
        var late = await ResolveSandboxAsync(anonymous, tenantId, orderId, authority, approved: true);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        var lateBody = (await late.Content.ReadFromJsonAsync<B043PaymentStatusDto>())!;
        Assert.Equal("Cancelled", lateBody.Status);
        Assert.Equal("Cancelled", (await ReadOrderRowAsync(orderId)).Status);
        Assert.Equal(1, await CountInvalidatedAttemptsAsync(orderId));
        Assert.Equal(0, await CountSucceededAttemptsAsync(orderId));
    }

    // ─── 4. Idempotent replay: same key + same action ─────────────────────────

    [Fact]
    public async Task Replay_SameKey_SameAction_ReturnsStoredResponse_WithoutRepeatingSideEffect()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Replay Shirt", $"rep-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Replay Customer", "09121000004");

        var key = Guid.NewGuid().ToString();
        var first = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = await first.Content.ReadAsStringAsync();

        var second = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondJson = await second.Content.ReadAsStringAsync();

        // The second call returns the stored final representation — byte-for-byte
        // identical to the first (the stored snapshot, re-serialized with the
        // endpoint's own JSON options).
        Assert.Equal(firstJson, secondJson);
        var secondBody = JsonDocument.Parse(secondJson).RootElement;
        Assert.Equal("Cancelled", secondBody.GetProperty("status").GetString());
        Assert.Equal(2, secondBody.GetProperty("version").GetInt32());

        // Exactly one operation row (the replay did not add a second).
        Assert.Equal(1, await CountOperationRowsAsync(orderId));
        // Stock restored exactly once despite two requests.
        Assert.Equal(initialStock, await ReadVariantStockAsync(variantId));
        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal(2, row.Version);
        Assert.NotNull(row.InventoryReleasedAtUtc);
    }

    // ─── 5. Same key, different action → conflict ─────────────────────────────

    [Fact]
    public async Task Replay_SameKey_DifferentAction_IsRejectedAsConflict_NothingPerformed()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Conflict Shirt", $"cf-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Conflict Customer", "09121000005");

        var key = Guid.NewGuid().ToString();
        var first = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Reuse the same key for a different action: a conflict, and the
        // Cancel is not undone nor re-applied.
        var conflict = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Fulfill", 2, key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<B043ProblemDto>()!;
        Assert.Equal("idempotency_key_conflict", conflictBody.Type);

        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("Cancelled", row.Status);
        Assert.Equal(2, row.Version);
        // Stock still restored exactly once; operation count unchanged.
        Assert.Equal(initialStock, await ReadVariantStockAsync(variantId));
        Assert.Equal(1, await CountOperationRowsAsync(orderId));
    }

    // ─── 6. Stale ExpectedVersion → rejected, no change ───────────────────────

    [Fact]
    public async Task StaleExpectedVersion_IsRejected_NoChangeApplied()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Stale Shirt", $"stale-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Stale Customer", "09121000006");

        // Version is 1 on creation; 99 is stale.
        var key = Guid.NewGuid().ToString();
        var stale = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 99, key);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<B043ProblemDto>()!;
        Assert.Equal("stale_version", staleBody.Type);

        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("PendingPayment", row.Status);
        Assert.Equal(1, row.Version);
        Assert.Null(row.CancelledAtUtc);
        Assert.Null(row.InventoryReleasedAtUtc);
        // No stock moved and no operation row was recorded.
        Assert.Equal(initialStock - 1, await ReadVariantStockAsync(variantId));
        Assert.Equal(0, await CountOperationRowsAsync(orderId));
    }

    // ─── 7. Two concurrent cancels → stock restored exactly once ──────────────

    [Fact]
    public async Task ConcurrentCancels_SameOrder_RestoreStockExactlyOnce()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, initialStock) = await CreateSingleVariantAsync(ownerClient, tenantId, "Race Shirt", $"race-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Race Customer", "09121000007");

        // Two concurrent cancel requests: one succeeds, one is rejected (it loses
        // the order-row lock race and re-reads the already-cancelled state).
        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(
            ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, keyA),
            ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, keyB));

        var statuses = results.Select(r => r.StatusCode).OrderBy(s => (int)s).ToList();
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.Conflict, statuses[1]);

        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("Cancelled", row.Status);
        Assert.Equal(2, row.Version);
        Assert.NotNull(row.InventoryReleasedAtUtc);

        // Stock restored exactly once: full initial quantity, not double-restored.
        Assert.Equal(initialStock, await ReadVariantStockAsync(variantId));
        // Only the winner recorded an operation row.
        Assert.Equal(1, await CountOperationRowsAsync(orderId));
    }

    // ─── 8. Permission matrix: Manage / View / Owner / no-permission / anon ───

    [Fact]
    public async Task Authorization_Matrix_ManageOwnerGranted_NoToken401_Others403()
    {
        var (tenantId, ownerClient, _, manageMember) = await NewTenantAsync();
        var (_, variantId, _) = await CreateSingleVariantAsync(ownerClient, tenantId, "Auth Shirt", $"auth2-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Auth Customer", "09121000008");

        var key = Guid.NewGuid().ToString();

        // Owner: 200 via bypass (no role grant).
        var ownerResult = await ChangeStatusAsync(ownerClient, tenantId, orderId, "Cancel", 1, key);
        Assert.Equal(HttpStatusCode.OK, ownerResult.StatusCode);
        Assert.Equal("Cancelled", (await ReadOrderRowAsync(orderId)).Status);

        // A fresh order for the matrix's remaining cases (the first is gone).
        var orderId2 = await CreateOrderAsync(tenantId, ownerClient, variantId, "Auth Customer 2", "09121000009");

        // A plain member without any Shop key: 403 (default deny).
        using var noKeyClient = CreateClient();
        SetMemberToken(noKeyClient, manageMember, $"nokey2-{manageMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ChangeStatusAsync(noKeyClient, tenantId, orderId2, "Cancel", 1, Guid.NewGuid().ToString())).StatusCode);

        // The same member granted Shop.Orders.View only: still 403 (View does not
        // unlock mutations).
        await GrantRoleAsync(tenantId, manageMember, ["Shop.Orders.View"]);
        using var viewOnlyClient = CreateClient();
        SetMemberToken(viewOnlyClient, manageMember, $"view2-{manageMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ChangeStatusAsync(viewOnlyClient, tenantId, orderId2, "Cancel", 1, Guid.NewGuid().ToString())).StatusCode);

        // A different member granted Shop.Orders.Manage: 200.
        var manageOnlyMember = await AddMemberAsync(tenantId);
        await GrantRoleAsync(tenantId, manageOnlyMember, ["Shop.Orders.Manage"]);
        using var manageClient = CreateClient();
        SetMemberToken(manageClient, manageOnlyMember, $"manage2-{manageOnlyMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.OK,
            (await ChangeStatusAsync(manageClient, tenantId, orderId2, "Cancel", 1, Guid.NewGuid().ToString())).StatusCode);

        // Anonymous: 401 (the JWT challenge answers before the handler runs).
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ChangeStatusAsync(anonymous, tenantId, orderId2, "Cancel", 1, Guid.NewGuid().ToString())).StatusCode);
    }

    // ─── 9. Cross-tenant order id → generic 404 ───────────────────────────────

    [Fact]
    public async Task CrossTenantOrderId_ReturnsTheSameGeneric404()
    {
        var (tenantA, clientA, _, _) = await NewTenantAsync();
        var (_, variantA, _) = await CreateSingleVariantAsync(clientA, tenantA, "Iso Shirt", $"iso2-{Guid.NewGuid():N}"[..14], 5, 500);
        var orderA = await CreateOrderAsync(tenantA, clientA, variantA, "Iso Customer", "09121000010");

        var (tenantB, clientB, _, _) = await NewTenantAsync();
        var (_, variantB, _) = await CreateSingleVariantAsync(clientB, tenantB, "Foreign Shirt", $"iso2b-{Guid.NewGuid():N}"[..14], 5, 500);
        var orderB = await CreateOrderAsync(tenantB, clientB, variantB, "Foreign Customer", "09121000011");

        var key = Guid.NewGuid().ToString();

        // Tenant A's operator attempts to cancel tenant B's order: the same
        // non-leaking generic 404 as a missing order id.
        var foreign = await ChangeStatusAsync(clientA, tenantA, orderB, "Cancel", 1, key);
        var missing = await ChangeStatusAsync(clientA, tenantA, TsidId.Format(TsidId.NewId()), "Cancel", 1, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());

        // Tenant B's order is untouched.
        Assert.Equal("PendingPayment", (await ReadOrderRowAsync(orderB)).Status);
    }

    // ─── Extra: validation (missing/invalid action, missing/invalid key) ─────

    [Fact]
    public async Task Validation_InvalidActionOrKey_Returns400NamingTheField()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var (_, variantId, _) = await CreateSingleVariantAsync(ownerClient, tenantId, "Val Shirt", $"val-{Guid.NewGuid():N}"[..14], 10, 1000);
        var orderId = await CreateOrderAsync(tenantId, ownerClient, variantId, "Val Customer", "09121000012");

        // Unknown action: 400 naming action.
        var badAction = await ChangeStatusRawAsync(ownerClient, tenantId, orderId,
            """{"action":"Refund","expectedVersion":1}""", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, badAction.StatusCode);
        Assert.Contains("\"action\"", await badAction.Content.ReadAsStringAsync());

        // Missing action: 400 naming action.
        var noAction = await ChangeStatusRawAsync(ownerClient, tenantId, orderId,
            """{"expectedVersion":1}""", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, noAction.StatusCode);
        Assert.Contains("\"action\"", await noAction.Content.ReadAsStringAsync());

        // Non-UUID idempotency key: 400 naming Idempotency-Key.
        var badKey = await ChangeStatusRawAsync(ownerClient, tenantId, orderId,
            """{"action":"Cancel","expectedVersion":1}""", "not-a-uuid");
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        Assert.Contains("Idempotency-Key", await badKey.Content.ReadAsStringAsync());

        // Missing idempotency key: 400 naming Idempotency-Key.
        var noKey = await ChangeStatusRawAsync(ownerClient, tenantId, orderId,
            """{"action":"Cancel","expectedVersion":1}""", null);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Contains("Idempotency-Key", await noKey.Content.ReadAsStringAsync());

        // Nothing was changed by any of the rejected requests.
        var row = await ReadOrderRowAsync(orderId);
        Assert.Equal("PendingPayment", row.Status);
        Assert.Equal(1, row.Version);
    }
}
