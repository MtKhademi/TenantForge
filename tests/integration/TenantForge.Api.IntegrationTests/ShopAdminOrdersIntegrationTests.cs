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
/// Protects B042's tenant-operator order reads: the permission-gated list
/// (filters, stable pagination) and detail (snapshot items, capped payment
/// attempts, non-leaking 404) plus the ShopOrder.Version column seeded for
/// B043's mutations.
///
/// Authorization matrix: an anonymous caller gets 401; an authenticated
/// non-member or a member without Shop.Orders.View gets 403; a member holding
/// the key and a tenant Owner get 200. Tenant isolation is proven with two
/// real tenants, and a cross-tenant order id 404s identically to a missing
/// one.
///
/// Runs on a dedicated database (ShopAdminOrdersIsolatedCollection) so its
/// tenants, products and orders never inflate the other Shop databases.
/// </summary>
[Collection(nameof(ShopAdminOrdersIsolatedCollection))]
public sealed class ShopAdminOrdersIntegrationTests(ShopAdminOrdersDbFixture db) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiFactory _factory = new(environment: "Development", seedMode: IamSeedMode.Complete, db);

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Reads a JSON body, asserting it is non-null once (the routes under
    /// test always return a body) so the call site does not need a trailing
    /// `!` that the compiler's nullable flow does not carry through.
    /// </summary>
    private static async Task<T> ReadJsonAsync<T>(Task<HttpResponseMessage> response) where T : class
    {
        var message = await response;
        var body = await message.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(body);
        return body!;
    }

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

    /// <summary>
    /// Authors a single-variant product and returns the product + variant ids.
    /// The variant id is read back through the anonymous product-detail route
    /// (id + stock + price) — the same way a shopper would see it.
    /// </summary>
    private async Task<B042VariantDto> CreateSingleVariantAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "ORDER", stockQuantity, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var productDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productId = productDocument.RootElement.GetProperty("id").GetString()!;

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<B042ProductDetailDto>())!;
        Assert.Single(detail.Variants);
        return new B042VariantDto(productId, detail.Variants[0].Id, name, basePrice);
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
        string customerName, string customerPhone, string? shippingProvince = "Tehran")
        => anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName,
            customerPhone,
            shippingProvince,
            shippingCity = "Tehran",
            shippingAddressLine = "Valiasr St.",
            shippingPostalCode = "1234567890"
        });

    /// <summary>
    /// Authors a fresh order through the real anonymous flow and returns its
    /// id + number. B031's order creation rejects a province with no shipping
    /// rate, so a rate for the order's province must exist first — this helper
    /// sets it through the admin API when <paramref name="ensureShippingRate"/>
    /// is set (tests that already set their own rate pass false and assert the
    /// exact cost).
    /// </summary>
    private async Task<B042OrderDto> CreateOrderAsync(
        string tenantId, HttpClient adminClient, string variantId,
        string customerName, string customerPhone, int quantity = 1, bool ensureShippingRate = true)
    {
        if (ensureShippingRate)
        {
            var rateResponse = await SetShippingRateAsync(adminClient, tenantId, "Tehran", 0m);
            Assert.Equal(HttpStatusCode.OK, rateResponse.StatusCode);
        }

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        var addResponse = await AddItemAsync(anonymous, tenantId, cartId, variantId, quantity);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var orderResponse = await PostOrderAsync(anonymous, tenantId, cartId, customerName, customerPhone);
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var created = (await orderResponse.Content.ReadFromJsonAsync<B042OrderCreatedDto>())!;
        return new B042OrderDto(created.OrderId, created.OrderNumber);
    }

    private async Task<int> CountPaymentAttemptsAsync(string orderId)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid), "orderId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM shop_payment_attempts WHERE order_id = @orderId;";
        command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value);
    }

    /// <summary>
    /// B044: the API caps a live order at 10 attempts, so a display-cap fact
    /// that needs more than 10 rows seeds them directly. Inserts
    /// <paramref name="count"/> synthetic Failed attempts with distinct,
    /// monotonically decreasing created_at_utc (i=0 is the newest) so the
    /// detail route's "20 newest, newest first" ordering is deterministic.
    /// </summary>
    private async Task SeedFailedPaymentAttemptsAsync(string orderId, int count)
    {
        Assert.True(TsidId.TryParse(orderId, out var orderTsid), "orderId must be a canonical TSID string");
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        // A far-out base keeps the synthetic ids clear of real TSIDs (which
        // are far smaller); the offset makes each unique within the insert.
        const long idBase = 9_000_000_000_000_000_000L;
        var newest = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO shop_payment_attempts (
                    id, order_id, provider, status, gateway_reference,
                    amount_snapshot, callback_token_hash, failure_code,
                    provider_reference, verified_at_utc, version,
                    created_at_utc, callback_received_at_utc)
                VALUES (
                    @id, @orderId, 'Sandbox', 'Failed', @ref,
                    200, @hash, 'payment_declined',
                    NULL, @now, 1,
                    @created, @now);
                """;
            command.Parameters.AddWithValue("id", idBase - i);
            command.Parameters.AddWithValue("orderId", orderTsid.ToLong());
            command.Parameters.AddWithValue("ref", $"seed-ref-{idBase - i}-x".PadRight(60, '0')[..60]);
            command.Parameters.AddWithValue("hash", new string('a', 64));
            command.Parameters.AddWithValue("now", newest.UtcDateTime);
            // i=0 is the newest; each subsequent row is strictly older.
            command.Parameters.AddWithValue("created", newest.UtcDateTime.AddSeconds(-i));
            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    // ─── 1. Authorization matrix ─────────────────────────────────────────────

    [Fact]
    public async Task Authorization_Matrix_ViewOwnerGranted_NoToken401_Other403()
    {
        var (tenantId, ownerClient, _, viewMember) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Auth Shirt", $"auth-{Guid.NewGuid():N}"[..14], 10, 1000);
        var order = await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "مریم رضایی", "09121111111");

        // Owner: 200 via bypass (no role grant).
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}")).StatusCode);

        // A plain member without any Shop key: 403 on both routes (default deny).
        using var noKeyClient = CreateClient();
        SetMemberToken(noKeyClient, viewMember, $"nokey-{viewMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await noKeyClient.GetAsync($"/api/tenants/{tenantId}/shop/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noKeyClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}")).StatusCode);

        // The same member, granted Shop.Orders.View: 200 on both routes.
        await GrantRoleAsync(tenantId, viewMember, ["Shop.Orders.View"]);
        using var viewClient = CreateClient();
        SetMemberToken(viewClient, viewMember, $"view-{viewMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.OK, (await viewClient.GetAsync($"/api/tenants/{tenantId}/shop/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}")).StatusCode);

        // A different member holding only Shop.Orders.Manage (reserved for
        // B043's mutations) must NOT unlock these reads.
        var manageMember = await AddMemberAsync(tenantId);
        await GrantRoleAsync(tenantId, manageMember, ["Shop.Orders.Manage"]);
        using var manageOnlyClient = CreateClient();
        SetMemberToken(manageOnlyClient, manageMember, $"manage-{manageMember.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await manageOnlyClient.GetAsync($"/api/tenants/{tenantId}/shop/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manageOnlyClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}")).StatusCode);

        // Anonymous: 401 on both routes.
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/tenants/{tenantId}/shop/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}")).StatusCode);
    }

    /// <summary>Adds one more plain member (no roles) to the tenant and returns its account id.</summary>
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

    // ─── 2. Tenant isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task TenantIsolation_ForeignOrdersNeverAppear_CrossTenantId404s()
    {
        var (tenantA, clientA, _, _) = await NewTenantAsync();
        var variantA = await CreateSingleVariantAsync(clientA, tenantA, "Isolation Shirt", $"iso-a-{Guid.NewGuid():N}"[..14], 5, 500);
        var orderA = await CreateOrderAsync(tenantA, clientA, variantA.VariantId, "Ali Isolation", "09122222222");

        var (tenantB, clientB, _, _) = await NewTenantAsync();
        var variantB = await CreateSingleVariantAsync(clientB, tenantB, "Foreign Shirt", $"iso-b-{Guid.NewGuid():N}"[..14], 5, 500);
        var orderB = await CreateOrderAsync(tenantB, clientB, variantB.VariantId, "Baba Foreign", "09123333333");

        // Tenant A's list never shows tenant B's order (by number or id).
        var listA = await ReadJsonAsync<B042AdminOrderListDto>(clientA.GetAsync($"/api/tenants/{tenantA}/shop/orders"));
        Assert.All(listA.Orders, o => Assert.NotEqual(orderB.OrderNumber, o.OrderNumber));
        Assert.All(listA.Orders, o => Assert.NotEqual(orderB.OrderId, o.Id));
        Assert.Contains(orderA.OrderNumber, listA.Orders.Select(o => o.OrderNumber));

        // A tenant-A operator cannot read tenant B's order: same generic 404
        // body as a missing one.
        var foreign = await clientA.GetAsync($"/api/tenants/{tenantA}/shop/orders/{orderB.OrderId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var missing = await clientA.GetAsync($"/api/tenants/{tenantA}/shop/orders/{TsidId.Format(TsidId.NewId())}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
    }

    // ─── 3. Filters: q / status / fromUtc / toUtc (valid + invalid) ─────────

    [Fact]
    public async Task Filters_ValidValuesNarrow_InvalidValuesReturn400()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Filter Shirt", $"flt-{Guid.NewGuid():N}"[..14], 20, 700);
        await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "Fateme Filter", "09124440001");
        await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "Kian Filter", "09124440002");

        // q matches customer name (case-insensitive) and phone; other tenants
        // would never match because the query is tenant-scoped.
        var byName = await ReadJsonAsync<B042AdminOrderListDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?q=FATEME"));
        Assert.Single(byName.Orders);
        Assert.Equal("Fateme Filter", byName.Orders[0].CustomerName);

        var byPhone = await ReadJsonAsync<B042AdminOrderListDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?q=09124440002"));
        Assert.Single(byPhone.Orders);
        Assert.Equal("Kian Filter", byPhone.Orders[0].CustomerName);

        // q longer than 100 chars: 400 naming q.
        var tooLong = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?q={new string('a', 101)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("\"q\"", await tooLong.Content.ReadAsStringAsync());

        // status: valid enum narrows; anything else is a 400 naming status.
        var paid = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?status=Paid");
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(0, (await paid.Content.ReadFromJsonAsync<B042AdminOrderListDto>())!.Pagination.TotalCount);

        var unknownStatus = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?status=Shipped");
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
        Assert.Contains("\"status\"", await unknownStatus.Content.ReadAsStringAsync());

        // fromUtc/toUtc: an empty window excludes both orders; a wide window
        // includes them (inclusive start / exclusive end are respected by the
        // window choice). `DateTimeOffset`'s `:O` form for a UTC value carries
        // a literal `+00:00`, which a raw query string decodes to a space
        // (a real client would send `%2B`), so the test percent-encodes the
        // value exactly as an HTTP client would before sending it.
        static string Enc(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

        var now = DateTimeOffset.UtcNow;
        var emptyWindow = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?fromUtc={Enc(now.AddHours(1))}&toUtc={Enc(now.AddHours(2))}");
        Assert.Equal(HttpStatusCode.OK, emptyWindow.StatusCode);
        Assert.Equal(0, (await emptyWindow.Content.ReadFromJsonAsync<B042AdminOrderListDto>())!.Pagination.TotalCount);

        var wideWindow = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?fromUtc={Enc(now.AddDays(-1))}&toUtc={Enc(now.AddDays(1))}");
        Assert.Equal(HttpStatusCode.OK, wideWindow.StatusCode);
        Assert.Equal(2, (await wideWindow.Content.ReadFromJsonAsync<B042AdminOrderListDto>())!.Pagination.TotalCount);

        // Malformed dates and an over-366-day range are 400s.
        var badFrom = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?fromUtc=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, badFrom.StatusCode);
        Assert.Contains("\"fromUtc\"", await badFrom.Content.ReadAsStringAsync());

        var badTo = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?toUtc=also-bad");
        Assert.Equal(HttpStatusCode.BadRequest, badTo.StatusCode);
        Assert.Contains("\"toUtc\"", await badTo.Content.ReadAsStringAsync());

        // Both dates parse here; the 400 must come from the range rule, not
        // from a parse failure (which the two malformed-date cases above cover).
        var tooWide = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?fromUtc={Enc(now.AddYears(-2))}&toUtc={Enc(now)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        Assert.Contains("\"toUtc\"", await tooWide.Content.ReadAsStringAsync());
    }

    // ─── 4. Stable pagination ────────────────────────────────────────────────

    [Fact]
    public async Task Pagination_TwoPagesWithSameFilters_StayStableAndComplete()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Page Shirt", $"pg-{Guid.NewGuid():N}"[..14], 30, 900);
        for (var i = 0; i < 4; i++)
        {
            await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, $"Page Customer {i}", $"0912555{i:0000}");
        }

        // Two pages of two, same filters, fetched twice: the second walk must
        // reproduce the first's exact ordering and contents (stable
        // pagination), and the union must be all 4 rows with no overlap.
        IReadOnlyList<string>? firstPassIds = null;
        for (var pass = 0; pass < 2; pass++)
        {
            var pageOne = await ReadJsonAsync<B042AdminOrderListDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?pageNumber=1&pageSize=2"));
            var pageTwo = await ReadJsonAsync<B042AdminOrderListDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders?pageNumber=2&pageSize=2"));

            Assert.Equal(4, pageOne.Pagination.TotalCount);
            Assert.Equal(2, pageOne.Pagination.TotalPages);
            Assert.False(pageOne.Pagination.HasPreviousPage);
            Assert.True(pageOne.Pagination.HasNextPage);
            Assert.True(pageTwo.Pagination.HasPreviousPage);
            Assert.False(pageTwo.Pagination.HasNextPage);

            var ids = pageOne.Orders.Select(o => o.Id).Concat(pageTwo.Orders.Select(o => o.Id)).ToList();
            Assert.Equal(4, ids.Distinct().Count());
            Assert.Equal(2, pageOne.Orders.Count);
            Assert.Equal(2, pageTwo.Orders.Count);

            if (pass == 0)
            {
                firstPassIds = ids;
                // The real invariant: newest first across the two pages
                // (non-increasing CreatedAtUtc, Id breaking ties).
                var combined = pageOne.Orders.Concat(pageTwo.Orders).ToList();
                for (var i = 0; i < combined.Count - 1; i++)
                {
                    Assert.True(combined[i].CreatedAtUtc >= combined[i + 1].CreatedAtUtc,
                        "orders must be returned newest-first");
                }
            }
            else
            {
                // Identical ordering and page contents on the second walk.
                Assert.Equal(firstPassIds, ids);
            }
        }
    }

    // ─── 5. Snapshot fidelity after the product changes ─────────────────────

    [Fact]
    public async Task Detail_KeepsTheOrderTimeSnapshot_AfterTheProductIsEdited()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Original Name", $"snap-{Guid.NewGuid():N}"[..14], 10, 1234);
        var order = await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "Snapshot Customer", "09126660006");

        // The detail shows the snapshot price (no override) and name.
        var before = await ReadJsonAsync<B042AdminOrderDetailDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}"));
        Assert.Equal(1234m, before.Totals.SubTotal);
        Assert.Single(before.Items);
        Assert.Equal("Original Name", before.Items[0].ProductNameSnapshot);
        Assert.Equal(1234m, before.Items[0].UnitPrice);
        Assert.Equal(1, before.Version);

        // Rename the product and change its price — the live catalog moved.
        var list = await ReadJsonAsync<B042ProductListDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/products"));
        var productId = list.Products.Single(p => p.Id == variant.ProductId).Id;
        var rename = await ownerClient.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}", new
        {
            name = "Renamed Product",
            slug = list.Products.Single(p => p.Id == productId).Slug,
            description = (string?)null,
            categoryId = list.Products.Single(p => p.Id == productId).CategoryId,
            basePrice = 9999,
            compareAtPrice = (decimal?)null,
            isActive = true,
            variants = new object[] { new { color = "Black", size = "M", sku = "SNAP", stockQuantity = 5, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // The stored order is untouched: same snapshot name and price.
        var after = await ReadJsonAsync<B042AdminOrderDetailDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}"));
        Assert.Equal("Original Name", after.Items[0].ProductNameSnapshot);
        Assert.Equal(1234m, after.Items[0].UnitPrice);
        Assert.Equal(1234m, after.Totals.SubTotal);
    }

    // ─── 6. Malformed / missing order id → generic 404 ───────────────────────

    [Fact]
    public async Task MalformedOrMissingOrderId_ReturnsTheSameGeneric404()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "404 Shirt", $"nf-{Guid.NewGuid():N}"[..14], 5, 100);
        var order = await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "NotFound Customer", "09127770007");

        var malformed = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/not-a-tsid");
        var missing = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{TsidId.Format(TsidId.NewId())}");
        var real = await ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}");

        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, real.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await malformed.Content.ReadAsStringAsync());
    }

    // ─── 7. Payment attempts capped at the 20 newest ─────────────────────────

    [Fact]
    public async Task Detail_ReturnsOnlyTheTwentyNewestPaymentAttempts()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Attempts Shirt", $"att-{Guid.NewGuid():N}"[..14], 25, 200);
        var order = await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "Attempts Customer", "09128880008");

        // B044: the API caps a live order at 10 attempts, so the 21 rows this
        // display-cap fact needs are seeded directly — synthetic Failed rows
        // with distinct, monotonically decreasing created_at_utc (i=0 is the
        // newest). The order stays PendingPayment; only the attempt history is
        // what the detail route must cap at its 20 newest.
        await SeedFailedPaymentAttemptsAsync(order.OrderId, count: 21);
        Assert.Equal(21, await CountPaymentAttemptsAsync(order.OrderId));

        var detail = await ReadJsonAsync<B042AdminOrderDetailDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}"));
        Assert.Equal(20, detail.PaymentAttempts.Count);
        // Newest first, every row a real Failed attempt with a canonical id.
        for (var i = 0; i < detail.PaymentAttempts.Count - 1; i++)
        {
            Assert.True(detail.PaymentAttempts[i].CreatedAtUtc >= detail.PaymentAttempts[i + 1].CreatedAtUtc);
        }
        Assert.All(detail.PaymentAttempts, a =>
        {
            Assert.Equal("Failed", a.Status);
            Assert.True(TsidId.TryParse(a.Id, out _));
        });
        // Oldest-first ids: the 20 returned must be the last 20 of 21, i.e.
        // the first (oldest) attempt is the only one missing.
        var returnedIds = detail.PaymentAttempts.Select(a => a.Id).ToHashSet();
        Assert.Equal(20, returnedIds.Count);
    }

    // ─── 8. Detail shape (customer, totals, items, version) ──────────────────

    [Fact]
    public async Task Detail_ReturnsCustomerTotalsItemsAndVersion()
    {
        var (tenantId, ownerClient, _, _) = await NewTenantAsync();
        var variant = await CreateSingleVariantAsync(ownerClient, tenantId, "Shape Shirt", $"shp-{Guid.NewGuid():N}"[..14], 5, 3000);
        await SetShippingRateAsync(ownerClient, tenantId, "Tehran", 350m);
        var order = await CreateOrderAsync(tenantId, ownerClient, variant.VariantId, "Shape Customer", "09129990009", ensureShippingRate: false);

        var detail = await ReadJsonAsync<B042AdminOrderDetailDto>(ownerClient.GetAsync($"/api/tenants/{tenantId}/shop/orders/{order.OrderId}"));

        Assert.Equal(order.OrderId, detail.Id);
        Assert.Equal(order.OrderNumber, detail.OrderNumber);
        Assert.Equal("PendingPayment", detail.Status);
        Assert.Equal(1, detail.Version);
        Assert.Equal("Shape Customer", detail.Customer.Name);
        Assert.Equal("09129990009", detail.Customer.Phone);
        Assert.Equal("Tehran", detail.Customer.ShippingProvince);
        Assert.Equal("Valiasr St.", detail.Customer.ShippingAddressLine);
        Assert.Equal(3000m, detail.Totals.SubTotal);
        Assert.Equal(350m, detail.Totals.ShippingCost);
        Assert.Equal(0m, detail.Totals.DiscountAmount);
        Assert.Equal(3350m, detail.Totals.GrandTotal);
        Assert.Single(detail.Items);
        Assert.Equal("Shape Shirt", detail.Items[0].ProductNameSnapshot);
        Assert.Equal(3000m, detail.Items[0].UnitPrice);
        Assert.False(string.IsNullOrWhiteSpace(detail.TrackingCode));
    }
}

// B042-prefixed DTOs: the test assembly is one namespace, so per-feature
// record names must not collide with the other Shop test files (trap #17).
internal sealed record B042VariantDto(string ProductId, string VariantId, string Name, decimal BasePrice);

internal sealed record B042StorefrontVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record B042ProductDetailDto(
    string Id, string CategoryId, string Name, string Slug, string Description,
    decimal BasePrice, decimal? CompareAtPrice,
    IReadOnlyList<B042StorefrontVariantDto> Variants);

internal sealed record B042OrderDto(string OrderId, string OrderNumber);

internal sealed record B042OrderCreatedDto(
    string OrderId, string OrderNumber, string TrackingCode, string Status,
    decimal SubTotal, decimal DiscountAmount, decimal ShippingCost, decimal GrandTotal);

internal sealed record B042AdminOrderSummaryDto(
    string Id, string OrderNumber, string CustomerName, string CustomerPhone,
    string Status, decimal GrandTotal, DateTimeOffset CreatedAtUtc);

internal sealed record B042PaginationDto(int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage);

internal sealed record B042AdminOrderListDto(IReadOnlyList<B042AdminOrderSummaryDto> Orders, B042PaginationDto Pagination);

internal sealed record B042AdminOrderCustomerDto(
    string Name, string Phone, string ShippingProvince, string ShippingCity,
    string ShippingAddressLine, string ShippingPostalCode);

internal sealed record B042AdminOrderTotalsDto(
    decimal SubTotal, decimal ShippingCost, decimal DiscountAmount, decimal GrandTotal);

internal sealed record B042AdminPaymentAttemptDto(string Id, string Status, DateTimeOffset CreatedAtUtc);

internal sealed record B042AdminOrderDetailDto(
    string Id, string OrderNumber, string TrackingCode, string Status,
    B042AdminOrderCustomerDto Customer, B042AdminOrderTotalsDto Totals,
    IReadOnlyList<OrderLookupItemDto> Items,
    IReadOnlyList<B042AdminPaymentAttemptDto> PaymentAttempts,
    int Version, DateTimeOffset CreatedAtUtc);

internal sealed record OrderLookupItemDto(
    string ProductNameSnapshot, string VariantLabelSnapshot, decimal UnitPrice, int Quantity);

internal sealed record B042ProductSummaryDto(string Id, string Name, string Slug, string CategoryId, decimal BasePrice, bool IsActive, int VariantCount);

internal sealed record B042ProductListDto(IReadOnlyList<B042ProductSummaryDto> Products, B042PaginationDto Pagination);
