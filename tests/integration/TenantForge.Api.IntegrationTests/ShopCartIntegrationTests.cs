using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Carts;
using TenantForge.Modules.Shop.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B028's anonymous cart surface and its core decision: adding an
/// item to a cart reserves live stock immediately through one guarded,
/// atomic UPDATE (EF Core ExecuteUpdateAsync), and removing/decreasing an
/// item releases it. Every fact authors catalog data through B026's
/// authenticated admin API, then drives the cart endpoints with a bare client
/// that sends no Authorization header — proving the routes are genuinely
/// anonymous while tenant isolation is enforced server-side by the {tenantId}
/// route segment on every query. Live stock is read back through B027's
/// anonymous product-detail endpoint, which reports the variant's current
/// StockQuantity.
///
/// Runs on a dedicated database (ShopCartIsolatedCollection) so its tenants,
/// carts and stock mutations never inflate the B026/B027 databases' row
/// counts or stock assertions.
/// </summary>
[Collection(nameof(ShopCartIsolatedCollection))]
public sealed class ShopCartIntegrationTests(ShopCartDbFixture db) : IDisposable
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
    /// Authors a single-variant product through B026's admin API and returns
    /// the anonymous product detail: the slug (for stock read-back) and the
    /// variant row (id + live stock + effective price at creation time). The
    /// detail read-back deliberately uses a bare client, mirroring how a
    /// shopper would actually read the live stock.
    /// </summary>
    private async Task<(string Slug, CartVariantDto Variant)> CreateSingleVariantProductAsync(
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
            variants = new object[] { new { color = "Black", size = "M", sku = "SINGLE", stockQuantity, priceOverride } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var anonymous = CreateClient();
        var detailResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<CartProductDetailDto>())!;
        return (slug, detail.Variants[0]);
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

    private static Task<HttpResponseMessage> UpdateItemAsync(
        HttpClient anonymous, string tenantId, string cartId, string itemId, int quantity)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/shop/{tenantId}/carts/{cartId}/items/{itemId}")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { quantity }), System.Text.Encoding.UTF8, "application/json")
        };
        return anonymous.SendAsync(request);
    }

    private static Task<HttpResponseMessage> RemoveItemAsync(
        HttpClient anonymous, string tenantId, string cartId, string itemId)
        => anonymous.DeleteAsync($"/api/shop/{tenantId}/carts/{cartId}/items/{itemId}");

    private static async Task<CartDto> GetCartAsync(HttpClient anonymous, string tenantId, string cartId)
    {
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CartDto>())!;
    }

    private static async Task<CartVariantDto> ReadVariantStockAsync(
        HttpClient anonymous, string tenantId, string slug, string variantId)
    {
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<CartProductDetailDto>())!;
        return detail.Variants.Single(variant => variant.Id == variantId);
    }

    [Fact]
    public async Task CreateCart_ReturnsNewCartId_AndFetchReturnsEmptyCartWithZeroSubtotal()
    {
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();

        var create = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var cartId = created.RootElement.GetProperty("cartId").GetString()!;
        Assert.True(TsidId.TryParse(cartId, out _), "cartId must be a canonical TSID string");

        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Equal(cartId, cart.CartId);
        Assert.Empty(cart.Items);
        Assert.Equal(0m, cart.SubTotal);
    }

    [Fact]
    public async Task AddItem_ReservesStock_SnapshotsEffectivePrice_AndReturnsTheCart()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Jackets", $"cat-{suffix}");
        var (slug, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Winter Jacket", $"jacket-{suffix}",
            stockQuantity: 3, basePrice: 500000, priceOverride: 450000);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);

        var response = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var cart = JsonSerializer.Deserialize<CartDto>(body, Json)!;

        // One line, the snapshot price is the variant's effective price (override
        // wins over base), the label is "Color / Size", and the subtotal matches.
        Assert.Equal(cartId, cart.CartId);
        Assert.Single(cart.Items);
        var line = cart.Items[0];
        Assert.Equal(variant.Id, line.ProductVariantId);
        Assert.Equal("Winter Jacket", line.ProductName);
        Assert.Equal("Black / M", line.VariantLabel);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(450000m, line.UnitPrice);
        Assert.Equal(450000m, cart.SubTotal);

        // The reservation is live in the database: B027's detail endpoint now
        // reports stock 3 -> 2 for this variant.
        var stock = await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id);
        Assert.Equal(2, stock.StockQuantity);
    }

    [Fact]
    public async Task AddingTheSameVariantTwice_MergesIntoOneLine_AndReservesTheAdditionalQuantity()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Scarves", $"cat-{suffix}");
        var (slug, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Wool Scarf", $"scarf-{suffix}",
            stockQuantity: 5, basePrice: 100000, priceOverride: null);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);

        var first = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 2);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        // The unique (cart_id, product_variant_id) index plus the merge rule:
        // one line holding the merged quantity, not two rows.
        Assert.Single(cart.Items);
        Assert.Equal(3, cart.Items[0].Quantity);
        Assert.Equal(300000m, cart.SubTotal);

        // 5 - 1 - 2 = 2 units still reserved-away from sale.
        var stock = await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id);
        Assert.Equal(2, stock.StockQuantity);
    }

    [Fact]
    public async Task TwoConcurrentAdds_AgainstTheLastUnit_ExactlyOneWins_AndStockNeverGoesNegative()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Hats", $"cat-{suffix}");
        var (slug, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Last Hat", $"hat-{suffix}",
            stockQuantity: 1, basePrice: 90000, priceOverride: null);

        using var shopperA = CreateClient();
        using var shopperB = CreateClient();
        var cart = await CreateCartAsync(shopperA, tenantId);

        var first = await AddItemAsync(shopperA, tenantId, cart, variant.Id, quantity: 1);
        var second = await AddItemAsync(shopperB, tenantId, cart, variant.Id, quantity: 1);

        var statuses = new[] { first.StatusCode, second.StatusCode }.OrderBy(s => (int)s).ToArray();
        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.Conflict, statuses[1]);

        // The winner's response carries the 409 title for the loser, and the
        // variant's final stock in the database is exactly 0 — never negative.
        var conflictBody = await (first.StatusCode == HttpStatusCode.Conflict ? first : second).Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(conflictBody);
        Assert.Equal("Insufficient stock", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());

        var stock = await ReadVariantStockAsync(shopperA, tenantId, slug, variant.Id);
        Assert.Equal(0, stock.StockQuantity);

        // And the cart holds exactly one unit of the variant, not two.
        var fetched = await GetCartAsync(shopperA, tenantId, cart);
        Assert.Single(fetched.Items);
        Assert.Equal(1, fetched.Items[0].Quantity);
    }

    [Fact]
    public async Task UpdateQuantity_ReservedWhenIncreased_ReleasedWhenDecreased_And409PastAvailableStock()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Bags", $"cat-{suffix}");
        var (slug, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Travel Bag", $"bag-{suffix}",
            stockQuantity: 5, basePrice: 250000, priceOverride: null);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        var added = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 2);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        var itemId = cart.Items[0].Id;

        // Increase 2 -> 4: reserves the extra 2 units (stock 3 -> 1).
        var increase = await UpdateItemAsync(anonymous, tenantId, cartId, itemId, quantity: 4);
        Assert.Equal(HttpStatusCode.OK, increase.StatusCode);
        Assert.Equal(1, (await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id)).StockQuantity);

        // Increase 4 -> 5 asks for exactly the one remaining unit: the guarded
        // UPDATE reserves it and the stock lawfully reaches 0 (never negative).
        var lastUnit = await UpdateItemAsync(anonymous, tenantId, cartId, itemId, quantity: 5);
        Assert.Equal(HttpStatusCode.OK, lastUnit.StatusCode);
        Assert.Equal(0, (await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id)).StockQuantity);

        // Increase 5 -> 6 asks for a unit that no longer exists: clean 409,
        // and the item quantity is unchanged (still 5, nothing new reserved).
        var pastStock = await UpdateItemAsync(anonymous, tenantId, cartId, itemId, quantity: 6);
        Assert.Equal(HttpStatusCode.Conflict, pastStock.StatusCode);
        Assert.Equal(5, (await GetCartAsync(anonymous, tenantId, cartId)).Items[0].Quantity);

        // Decrease 5 -> 2: releases three units (stock 0 -> 3).
        var decrease = await UpdateItemAsync(anonymous, tenantId, cartId, itemId, quantity: 2);
        Assert.Equal(HttpStatusCode.OK, decrease.StatusCode);
        var after = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Equal(2, after.Items[0].Quantity);
        Assert.Equal(3, (await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id)).StockQuantity);

        // A non-positive quantity is a validation problem, not a removal.
        var zero = await UpdateItemAsync(anonymous, tenantId, cartId, itemId, quantity: 0);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
    }

    [Fact]
    public async Task RemoveItem_ReleasesItsReservedStockBackOntoTheVariant()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Belts", $"cat-{suffix}");
        var (slug, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Leather Belt", $"belt-{suffix}",
            stockQuantity: 4, basePrice: 120000, priceOverride: null);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        var added = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 3);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(1, (await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id)).StockQuantity);

        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        var removed = await RemoveItemAsync(anonymous, tenantId, cartId, cart.Items[0].Id);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var after = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Empty(after.Items);
        Assert.Equal(0m, after.SubTotal);
        // The full reservation went back: stock is 4 again.
        Assert.Equal(4, (await ReadVariantStockAsync(anonymous, tenantId, slug, variant.Id)).StockQuantity);

        // Removing an already-removed item is a 404, not an error.
        var again = await RemoveItemAsync(anonymous, tenantId, cartId, cart.Items[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task GetCart_WithATenantMismatch_OrUnknownCartId_Returns404()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, _) = await NewTenantWithOwnerAsync();

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantA);

        // A is readable by A...
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/shop/{tenantA}/carts/{cartId}")).StatusCode);

        // ...but the same cart id under B's segment is a clean 404: the
        // existence check is scoped by the route tenantId.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shop/{tenantB}/carts/{cartId}")).StatusCode);

        // A canonical but nonexistent cart id is also 404.
        var unknownCart = TsidId.Format(TsidId.NewId());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shop/{tenantA}/carts/{unknownCart}")).StatusCode);
        // A malformed cart id is 404 as well — never 400 or 500.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shop/{tenantA}/carts/not-a-cart")).StatusCode);
    }

    [Fact]
    public async Task InvalidVariantsAndQuantities_ValidateBeforeAnyStockTouch()
    {
        var (tenantId, _) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);

        // No variant at all: 400 naming the field.
        var missing = await AddItemAsync(anonymous, tenantId, cartId, variantId: null!, quantity: 1);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingBody = await missing.Content.ReadAsStringAsync();
        Assert.Contains("productVariantId", missingBody, StringComparison.Ordinal);

        // Unknown-but-well-formed variant id: 404 (no stock touch possible).
        var unknown = await AddItemAsync(anonymous, tenantId, cartId, TsidId.Format(TsidId.NewId()), quantity: 1);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Zero/negative quantity: 400 before any stock reservation.
        var zero = await AddItemAsync(anonymous, tenantId, cartId, variantId: null!, quantity: 0);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        var negative = await AddItemAsync(anonymous, tenantId, cartId, variantId: null!, quantity: -2);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task Subtotal_EqualsSumOfSnapshotPriceTimesQuantity_AcrossItems()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shoes", $"cat-{suffix}");
        var (slugA, variantA) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Runner A", $"runner-a-{suffix}",
            stockQuantity: 10, basePrice: 300000, priceOverride: null);
        var (slugB, variantB) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Runner B", $"runner-b-{suffix}",
            stockQuantity: 10, basePrice: 400000, priceOverride: 350000);

        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantA.Id, quantity: 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variantB.Id, quantity: 3)).StatusCode);

        var cart = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Equal(2, cart.Items.Count);
        Assert.Equal(2 * 300000m + 3 * 350000m, cart.SubTotal);

        // Bump A to 4: subtotal recomputes from the snapshot price, and stock
        // reflects both reservations (A: 10-2-2=6, B: 10-3=7).
        var itemIdA = cart.Items.Single(item => item.ProductVariantId == variantA.Id).Id;
        Assert.Equal(HttpStatusCode.OK, (await UpdateItemAsync(anonymous, tenantId, cartId, itemIdA, quantity: 4)).StatusCode);
        var after = await GetCartAsync(anonymous, tenantId, cartId);
        Assert.Equal(4 * 300000m + 3 * 350000m, after.SubTotal);
        Assert.Equal(6, (await ReadVariantStockAsync(anonymous, tenantId, slugA, variantA.Id)).StockQuantity);
        Assert.Equal(7, (await ReadVariantStockAsync(anonymous, tenantId, slugB, variantB.Id)).StockQuantity);
    }

    [Fact]
    public async Task AllCartRoutes_AreAnonymous_NoAuthorizationHeaderRequired()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Caps", $"cat-{suffix}");
        var (_, variant) = await CreateSingleVariantProductAsync(
            member, tenantId, categoryId, "Logo Cap", $"cap-{suffix}",
            stockQuantity: 2, basePrice: 80000, priceOverride: null);

        // A bare client sends no Authorization header at all.
        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        var created = await anonymous.PostAsync($"/api/shop/{tenantId}/carts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var cartId = doc.RootElement.GetProperty("cartId").GetString()!;

        var added = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, quantity: 1);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var fetched = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartId}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task MutatingCart_ExtendsLeaseAndReadDoesNotExtendIt()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Lease", $"cat-{suffix}");
        var (_, variant) = await CreateSingleVariantProductAsync(member, tenantId, categoryId, "Lease Item", $"lease-{suffix}", 5, 100m, null);
        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.True(TsidId.TryParse(cartId, out var cartTsid));

        await using (var shop = CreateShopContext())
        {
            var cart = await shop.Carts.SingleAsync(cart => cart.Id == cartTsid);
            cart.ExtendLease(DateTimeOffset.UtcNow.AddMinutes(-20), TimeSpan.FromMinutes(30));
            await shop.SaveChangesAsync();
        }

        var before = await GetCartRowAsync(cartTsid);
        var added = await AddItemAsync(anonymous, tenantId, cartId, variant.Id, 1);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var afterMutation = await GetCartRowAsync(cartTsid);
        Assert.True(afterMutation.LastTouchedAtUtc > before.LastTouchedAtUtc);
        Assert.True(afterMutation.ExpiresAtUtc > before.ExpiresAtUtc);

        _ = await GetCartAsync(anonymous, tenantId, cartId);
        var afterRead = await GetCartRowAsync(cartTsid);
        Assert.Equal(afterMutation.LastTouchedAtUtc, afterRead.LastTouchedAtUtc);
        Assert.Equal(afterMutation.ExpiresAtUtc, afterRead.ExpiresAtUtc);
    }

    [Fact]
    public async Task ExpireDue_RestoresStockOnce_RespectsBatchSize_AndTenantIsolation()
    {
        var (tenantA, memberA) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB) = await NewTenantWithOwnerAsync();
        using var anonymous = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var catA = await CreateCategoryAsync(memberA, tenantA, "A", $"cat-a-{suffix}");
        var catB = await CreateCategoryAsync(memberB, tenantB, "B", $"cat-b-{suffix}");
        var (slugA, variantA) = await CreateSingleVariantProductAsync(memberA, tenantA, catA, "A", $"a-{suffix}", 10, 100m, null);
        var (slugB, variantB) = await CreateSingleVariantProductAsync(memberB, tenantB, catB, "B", $"b-{suffix}", 10, 100m, null);
        var cartA = await CreateCartAsync(anonymous, tenantA);
        var cartB = await CreateCartAsync(anonymous, tenantB);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantA, cartA, variantA.Id, 3)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantB, cartB, variantB.Id, 4)).StatusCode);
        Assert.True(TsidId.TryParse(cartA, out var cartATsid));
        Assert.True(TsidId.TryParse(cartB, out var cartBTsid));

        await SetCartExpiryAsync(cartATsid, DateTimeOffset.UtcNow.AddMinutes(-5));
        await SetCartExpiryAsync(cartBTsid, DateTimeOffset.UtcNow.AddMinutes(-5));

        var service = new ShopCartExpiryService(CreateShopContext(), TimeProvider.System);
        Assert.Equal(1, await service.ExpireDueAsync(batchSize: 1, CancellationToken.None));
        Assert.Equal(1, await service.ExpireDueAsync(batchSize: 100, CancellationToken.None));
        Assert.Equal(0, await service.ExpireDueAsync(batchSize: 100, CancellationToken.None));

        Assert.Equal(10, (await ReadVariantStockAsync(anonymous, tenantA, slugA, variantA.Id)).StockQuantity);
        Assert.Equal(10, (await ReadVariantStockAsync(anonymous, tenantB, slugB, variantB.Id)).StockQuantity);
        Assert.Equal(ShopCartStatus.Expired, (await GetCartRowAsync(cartATsid)).Status);
        Assert.Equal(ShopCartStatus.Expired, (await GetCartRowAsync(cartBTsid)).Status);
    }

    [Fact]
    public async Task ExpiredCart_ReturnsGoneProblemOnCartCheckoutAndOrderRoutes()
    {
        var (tenantId, member) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Expired", $"cat-{suffix}");
        var (_, variant) = await CreateSingleVariantProductAsync(member, tenantId, categoryId, "Expired Item", $"expired-{suffix}", 5, 100m, null);
        await SetShippingRateAsync(member, tenantId);
        using var anonymous = CreateClient();
        var cartId = await CreateCartAsync(anonymous, tenantId);
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(anonymous, tenantId, cartId, variant.Id, 1)).StatusCode);
        Assert.True(TsidId.TryParse(cartId, out var cartTsid));
        await SetCartExpiryAsync(cartTsid, DateTimeOffset.UtcNow.AddMinutes(-5));

        var get = await anonymous.GetAsync($"/api/shop/{tenantId}/carts/{cartId}");
        await AssertCartExpiredProblemAsync(get);

        var checkout = await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/checkout/summary", new { cartId, shippingProvince = "Tehran", couponCode = (string?)null });
        await AssertCartExpiredProblemAsync(checkout);

        var order = await anonymous.PostAsJsonAsync($"/api/shop/{tenantId}/orders", new
        {
            cartId,
            customerName = "Customer",
            customerPhone = "09120000000",
            shippingProvince = "Tehran",
            shippingCity = "Tehran",
            shippingAddressLine = "Address",
            shippingPostalCode = "12345",
            couponCode = (string?)null
        });
        await AssertCartExpiredProblemAsync(order);
    }

    private ShopDbContext CreateShopContext() => new(
        new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(db.ConnectionString).Options);

    private async Task<(ShopCartStatus Status, DateTimeOffset LastTouchedAtUtc, DateTimeOffset ExpiresAtUtc)> GetCartRowAsync(Tsid cartId)
    {
        await using var shop = CreateShopContext();
        return await shop.Carts
            .Where(cart => cart.Id == cartId)
            .Select(cart => new ValueTuple<ShopCartStatus, DateTimeOffset, DateTimeOffset>(cart.Status, cart.LastTouchedAtUtc, cart.ExpiresAtUtc))
            .SingleAsync();
    }

    private async Task SetCartExpiryAsync(Tsid cartId, DateTimeOffset expiresAtUtc)
    {
        await using var shop = CreateShopContext();
        var cart = await shop.Carts.SingleAsync(cart => cart.Id == cartId);
        cart.ExtendLease(expiresAtUtc.AddMinutes(-30), TimeSpan.FromMinutes(30));
        await shop.SaveChangesAsync();
    }

    private static async Task SetShippingRateAsync(HttpClient memberClient, string tenantId)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/shipping-rates", new { provinceName = "Tehran", cost = 10m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertCartExpiredProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("shop_cart_expired", document.RootElement.GetProperty("type").GetString());
    }
}

internal sealed record CartVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record CartProductDetailDto(
    string Id, string Name, string Slug, IReadOnlyList<CartVariantDto> Variants);

internal sealed record CartItemDto(
    string Id, string ProductVariantId, string ProductName, string VariantLabel, int Quantity, decimal UnitPrice);

internal sealed record CartDto(string CartId, IReadOnlyList<CartItemDto> Items, decimal SubTotal, DateTimeOffset ExpiresAtUtc);
