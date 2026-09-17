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
/// Protects B027's anonymous storefront read surface: the first endpoints in
/// TenantForge that require no authentication at all. Every fact authors data
/// through B026's authenticated admin API (a real tenant owner's JWT), then
/// reads it back with a bare client that sends no Authorization header, proving
/// the routes are genuinely anonymous and tenant-scoped only by the {tenantId}
/// route segment. A malformed tenantId is a clean 404 (never a 400/500), and an
/// inactive product/category never appears — exactly like a nonexistent one.
///
/// Runs on a dedicated database (ShopStorefrontIsolatedCollection) so its
/// tenants/categories/products never inflate the B026 admin DB's row counts.
/// </summary>
[Collection(nameof(ShopStorefrontIsolatedCollection))]
public sealed class ShopStorefrontCatalogIntegrationTests(ShopStorefrontDbFixture db) : IDisposable
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

    /// <summary>A real, non-platform-admin JWT for the given account (a tenant owner).</summary>
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

    /// <summary>Authored through B026's authenticated admin API (the caller must be a member).</summary>
    private static async Task<string> CreateCategoryAsync(HttpClient memberClient, string tenantId, string name, string slug, int displayOrder = 1)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name,
            slug,
            displayOrder
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Authored through B026's authenticated admin API. Returns the product id.</summary>
    private static async Task<string> CreateProductAsync(
        HttpClient memberClient, string tenantId, string categoryId, string name, string slug,
        object? variants = null, object? sizeGuideColumns = null, object? sizeGuideRows = null)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice = 100,
            compareAtPrice = (decimal?)null,
            variants = variants ?? new object[] { new { color = "White", size = "M", sku = "P", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)sizeGuideColumns,
            sizeGuideRows = (object?)sizeGuideRows
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<(string TenantId, HttpClient MemberClient, Tsid OwnerAccount)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var memberClient = CreateClient();
        SetMemberToken(memberClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, memberClient, ownerAccount);
    }

    [Fact]
    public async Task ActiveCategories_AreListedInDisplayOrder_AndInactiveAreExcluded()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];

        // Three active categories, deliberately created out of order.
        await CreateCategoryAsync(member, tenantId, "Scarves", $"scarves-{suffix}", displayOrder: 3);
        var shirtsId = await CreateCategoryAsync(member, tenantId, "Shirts", $"shirts-{suffix}", displayOrder: 1);
        await CreateCategoryAsync(member, tenantId, "Dresses", $"dresses-{suffix}", displayOrder: 2);

        // Deactivate the middle one through the admin API.
        var deactivate = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{shirtsId}", new
        {
            name = "Shirts",
            slug = $"shirts-{suffix}",
            displayOrder = 1,
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // Anonymous read: only the two active categories, in displayOrder order.
        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<StorefrontCategoryListDto>();
        Assert.NotNull(list);
        Assert.Equal(2, list!.Categories.Count);
        Assert.Equal("Dresses", list.Categories[0].Name);
        Assert.Equal("Scarves", list.Categories[1].Name);
        Assert.DoesNotContain(list.Categories, category => category.Slug == $"shirts-{suffix}");
    }

    [Fact]
    public async Task ProductsWithinCategory_AreListedPaginated_AndSummaryUsesBasePriceAsEffective()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");
        var categorySlug = $"cat-{suffix}";

        var slugA = $"alpha-{suffix}";
        var slugB = $"bravo-{suffix}";
        var slugC = $"charlie-{suffix}";
        await CreateProductAsync(member, tenantId, categoryId, "Alpha Shirt", slugA);
        await CreateProductAsync(member, tenantId, categoryId, "Bravo Shirt", slugB);
        await CreateProductAsync(member, tenantId, categoryId, "Charlie Shirt", slugC);

        using var anonymous = CreateClient();
        var page1 = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/{categorySlug}/products?pageNumber=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
        var page1Body = await page1.Content.ReadAsStringAsync();
        var page1List = JsonSerializer.Deserialize<StorefrontProductListDto>(page1Body, Json);
        Assert.NotNull(page1List);
        Assert.Equal(2, page1List!.Products.Count);
        // Summary effectivePrice is the product BasePrice (variant overrides are a
        // detail-page concern); the shared helper sets basePrice=100.
        foreach (var product in page1List.Products)
        {
            Assert.Equal(100m, product.EffectivePrice);
            Assert.Null(product.CompareAtPrice);
        }
        Assert.Equal(3, page1List.Pagination.TotalCount);
        Assert.Equal(2, page1List.Pagination.TotalPages);
        Assert.True(page1List.Pagination.HasNextPage);
        Assert.False(page1List.Pagination.HasPreviousPage);
        // Ordered by Name then Id: "Alpha Shirt" then "Bravo Shirt".
        Assert.Equal("Alpha Shirt", page1List.Products[0].Name);
        Assert.Equal("Bravo Shirt", page1List.Products[1].Name);
        Assert.DoesNotContain("\"sku\"", page1Body, StringComparison.Ordinal);

        var page2 = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/{categorySlug}/products?pageNumber=2&pageSize=2");
        var page2List = await page2.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(page2List);
        Assert.Single(page2List!.Products);
        Assert.Equal("Charlie Shirt", page2List.Products[0].Name);
        Assert.True(page2List.Pagination.HasPreviousPage);
        Assert.False(page2List.Pagination.HasNextPage);
    }

    [Fact]
    public async Task ProductDetailBySlug_ReturnsLiveStock_EffectivePriceAndSizeGuide_WithNoSkuField()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");
        var productSlug = $"classic-{suffix}";

        var create = await member.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Classic Shirt",
            slug = productSlug,
            description = "A cotton classic",
            categoryId,
            basePrice = 890000,
            compareAtPrice = 990000,
            variants = new object[]
            {
                new { color = "White", size = "L", sku = "SHIRT-WHT-L", stockQuantity = 5, priceOverride = 120000.0 },
                new { color = "White", size = "M", sku = "SHIRT-WHT-M", stockQuantity = 10, priceOverride = (decimal?)null }
            },
            sizeGuideColumns = new[] { "chest", "waist" },
            sizeGuideRows = new object[]
            {
                new { sizeLabel = "M", values = new[] { "96", "80" } },
                new { sizeLabel = "L", values = new[] { "102", "86" } }
            }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<StorefrontProductDetailDto>(body, Json);
        Assert.NotNull(detail);

        // Header fields, with the category id exposed (never the tenant id).
        Assert.Equal(productSlug, detail!.Slug);
        Assert.Equal("A cotton classic", detail.Description);
        Assert.Equal(890000m, detail.BasePrice);
        Assert.Equal(990000m, detail.CompareAtPrice);
        Assert.True(TsidId.TryParse(detail.CategoryId, out _));
        Assert.DoesNotContain("tenantId", body, StringComparison.OrdinalIgnoreCase);

        // Live stock + effective price: the L variant uses its override, the M
        // variant falls back to the base price.
        Assert.Equal(2, detail.Variants.Count);
        var large = detail.Variants.Single(variant => variant.Size == "L");
        Assert.Equal(5, large.StockQuantity);
        Assert.Equal(120000m, large.EffectivePrice);
        var medium = detail.Variants.Single(variant => variant.Size == "M");
        Assert.Equal(10, medium.StockQuantity);
        Assert.Equal(890000m, medium.EffectivePrice);

        // Size-guide table shape: two named columns, two rows, a value per cell.
        Assert.Equal(2, detail.SizeGuideColumns.Count);
        var chestId = detail.SizeGuideColumns[0].Id;
        var waistId = detail.SizeGuideColumns[1].Id;
        var mRow = detail.SizeGuideRows.Single(row => row.SizeLabel == "M");
        Assert.Contains(new StorefrontCellDto(chestId, "96"), mRow.Cells);
        Assert.Contains(new StorefrontCellDto(waistId, "80"), mRow.Cells);
        var lRow = detail.SizeGuideRows.Single(row => row.SizeLabel == "L");
        Assert.Contains(new StorefrontCellDto(chestId, "102"), lRow.Cells);
        Assert.Contains(new StorefrontCellDto(waistId, "86"), lRow.Cells);

        // The storefront response deliberately omits the admin-only Sku field.
        Assert.DoesNotContain("\"sku\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InactiveProductSlug_Returns404_JustLikeANonexistentOne()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");
        var productSlug = $"hidden-{suffix}";
        var productId = await CreateProductAsync(member, tenantId, categoryId, "Hidden Shirt", productSlug);

        // While active, it is visible.
        using var anonymous = CreateClient();
        var visible = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        // Deactivate it through the admin API.
        var deactivate = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}", new
        {
            name = "Hidden Shirt",
            slug = productSlug,
            description = (string?)null,
            categoryId,
            basePrice = 100,
            compareAtPrice = (decimal?)null,
            isActive = false,
            variants = new object[] { new { color = "White", size = "M", sku = "H", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        // Now the same slug is a 404, indistinguishable from a slug that never
        // existed — an anonymous caller learns nothing about the product.
        var hidden = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var neverExisted = await anonymous.GetAsync($"/api/shop/{tenantId}/products/never-existed-{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, neverExisted.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_AProductIsNeverVisibleUnderAnotherTenant()
    {
        // Tenant A authors a category and a product.
        var (tenantA, memberA, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryA = await CreateCategoryAsync(memberA, tenantA, "Shirts", $"cat-a-{suffix}");
        var productSlug = $"a-shirt-{suffix}";
        await CreateProductAsync(memberA, tenantA, categoryA, "A Shirt", productSlug);

        // Tenant B is a completely separate boutique with its own (empty) catalog.
        var (tenantB, memberB, _) = await NewTenantWithOwnerAsync();

        using var anonymous = CreateClient();

        // The product exists for its own tenant...
        var own = await anonymous.GetAsync($"/api/shop/{tenantA}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        // ...but requesting that same slug under tenant B is a clean 404 — the
        // query is scoped by the route tenantId, so A's row is never reachable
        // through B's segment.
        var cross = await anonymous.GetAsync($"/api/shop/{tenantB}/products/{productSlug}");
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);

        // Same isolation through the category-products route: A's category slug
        // is unknown to B.
        var crossCategory = await anonymous.GetAsync($"/api/shop/{tenantB}/categories/cat-a-{suffix}/products");
        Assert.Equal(HttpStatusCode.NotFound, crossCategory.StatusCode);

        // And B's own category list is empty, not A's rows.
        var bCategories = await anonymous.GetAsync($"/api/shop/{tenantB}/categories");
        var bList = await bCategories.Content.ReadFromJsonAsync<StorefrontCategoryListDto>();
        Assert.NotNull(bList);
        Assert.Empty(bList!.Categories);
    }

    [Fact]
    public async Task AllStorefrontRoutes_AreAnonymous_NoAuthorizationHeaderRequired()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");
        var productSlug = $"anon-{suffix}";
        await CreateProductAsync(member, tenantId, categoryId, "Anon Shirt", productSlug);

        // A bare client sends no Authorization header at all.
        using var anonymous = CreateClient();
        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        var categories = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        var products = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/cat-{suffix}/products");
        var detail = await anonymous.GetAsync($"/api/shop/{tenantId}/products/{productSlug}");

        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        // A malformed (non-TSID) tenantId is a clean 404 — never 400, 401 or 500.
        var badTenant = await anonymous.GetAsync("/api/shop/not-a-tsid/categories");
        Assert.Equal(HttpStatusCode.NotFound, badTenant.StatusCode);
    }
}

internal sealed record StorefrontCategoryDto(string Id, string Name, string Slug, int DisplayOrder);

internal sealed record StorefrontCategoryListDto(IReadOnlyList<StorefrontCategoryDto> Categories);

internal sealed record StorefrontProductSummaryDto(string Id, string Name, string Slug, decimal EffectivePrice, decimal? CompareAtPrice);

internal sealed record StorefrontProductListDto(IReadOnlyList<StorefrontProductSummaryDto> Products, ShopPaginationDto Pagination);

internal sealed record StorefrontVariantDto(string Id, string Color, string Size, int StockQuantity, decimal EffectivePrice);

internal sealed record StorefrontSizeGuideColumnDto(string Id, string Name, int DisplayOrder);

internal sealed record StorefrontCellDto(string ColumnId, string Value);

internal sealed record StorefrontSizeGuideRowDto(string SizeLabel, int DisplayOrder, IReadOnlyList<StorefrontCellDto> Cells);

internal sealed record StorefrontProductDetailDto(
    string Id,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    IReadOnlyList<StorefrontVariantDto> Variants,
    IReadOnlyList<StorefrontSizeGuideColumnDto> SizeGuideColumns,
    IReadOnlyList<StorefrontSizeGuideRowDto> SizeGuideRows);
