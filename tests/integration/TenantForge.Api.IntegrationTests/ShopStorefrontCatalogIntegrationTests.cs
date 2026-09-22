using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
        object? variants = null, object? sizeGuideColumns = null, object? sizeGuideRows = null,
        decimal basePrice = 100, decimal? compareAtPrice = null)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice,
            compareAtPrice = (decimal?)compareAtPrice,
            variants = variants ?? new object[] { new { color = "White", size = "M", sku = "P", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)sizeGuideColumns,
            sizeGuideRows = (object?)sizeGuideRows
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    // ---- Real, in-memory-generated image fixtures (B036 media API) -------

    /// <summary>A minimal, valid opaque JPEG of the given size.</summary>
    private static byte[] MakeJpeg(int width = 32, int height = 32)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 40, 200));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static MultipartFormDataContent BuildUploadForm(byte[] bytes, string altText, int expectedGalleryVersion)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "File", "photo.jpg");
        if (altText is not null)
        {
            form.Add(new StringContent(altText), "AltText");
        }
        form.Add(new StringContent(expectedGalleryVersion.ToString()), "ExpectedGalleryVersion");
        return form;
    }

    private static Task<HttpResponseMessage> UploadImageAsync(HttpClient client, string tenantId, string productId, int expectedGalleryVersion, string altText = "Front") =>
        client.PostAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images", BuildUploadForm(MakeJpeg(), altText, expectedGalleryVersion));

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

    // ---- B037: GET /api/shop/{tenantId}/products — search, sort, sale ----

    private static async Task<StorefrontProductListDto> ReadListAsync(HttpResponseMessage response)
    {
        var list = await response.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(list);
        return list!;
    }

    private static async Task<(HttpStatusCode Status, StorefrontProductListDto List)> GetHttpProductsAsync(HttpClient client, string tenantId, string queryString)
    {
        var response = await client.GetAsync($"/api/shop/{tenantId}/products{queryString}");
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonSerializer.Deserialize<StorefrontProductListDto>(body, Json)!);
    }

    [Fact]
    public async Task AllProducts_EachTenantOnlySeesItsOwnCatalog()
    {
        var (tenantA, memberA, _) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];

        var catA = await CreateCategoryAsync(memberA, tenantA, "Shirts", $"cat-a-{suffix}");
        var catB = await CreateCategoryAsync(memberB, tenantB, "Shirts", $"cat-b-{suffix}");
        await CreateProductAsync(memberA, tenantA, catA, "Alpha Tee", $"alpha-{suffix}");
        await CreateProductAsync(memberB, tenantB, catB, "Bravo Tee", $"bravo-{suffix}");

        using var anonymous = CreateClient();
        var (aStatus, aList) = await GetHttpProductsAsync(anonymous, tenantA, "?sort=name");
        Assert.Equal(HttpStatusCode.OK, aStatus);
        Assert.Single(aList.Products);
        Assert.Equal("Alpha Tee", aList.Products[0].Name);
        Assert.Equal(1, aList.Pagination.TotalCount);

        var (bStatus, bList) = await GetHttpProductsAsync(anonymous, tenantB, "?sort=name");
        Assert.Equal(HttpStatusCode.OK, bStatus);
        Assert.Single(bList.Products);
        Assert.Equal("Bravo Tee", bList.Products[0].Name);
    }

    [Fact]
    public async Task AllProducts_InactiveCategoryAndInactiveProductAreExcluded()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];

        var activeCat = await CreateCategoryAsync(member, tenantId, "Active", $"active-{suffix}");
        var inactiveCat = await CreateCategoryAsync(member, tenantId, "Inactive", $"inactive-{suffix}");
        var deactivateCat = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{inactiveCat}", new
        {
            name = "Inactive", slug = $"inactive-{suffix}", displayOrder = 2, isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, deactivateCat.StatusCode);

        await CreateProductAsync(member, tenantId, activeCat, "Shown Tee", $"shown-{suffix}");
        await CreateProductAsync(member, tenantId, inactiveCat, "HiddenByCat Tee", $"hiddencat-{suffix}");

        // An active product whose own row is deactivated.
        var hiddenId = await CreateProductAsync(member, tenantId, activeCat, "Hidden Tee", $"hidden-{suffix}");
        var deactivateProduct = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{hiddenId}", new
        {
            name = "Hidden Tee", slug = $"hidden-{suffix}", description = (string?)null, categoryId = activeCat,
            basePrice = 100, compareAtPrice = (decimal?)null, isActive = false,
            variants = new object[] { new { color = "White", size = "M", sku = "H", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null, sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.OK, deactivateProduct.StatusCode);

        using var anonymous = CreateClient();
        var (status, list) = await GetHttpProductsAsync(anonymous, tenantId, "?sort=name");
        Assert.Equal(HttpStatusCode.OK, status);
        var names = list.Products.Select(p => p.Name).ToList();
        Assert.Contains("Shown Tee", names);
        Assert.DoesNotContain("HiddenByCat Tee", names); // product in an inactive category
        Assert.DoesNotContain("Hidden Tee", names);      // inactive product row
    }

    [Fact]
    public async Task AllProducts_QueryIsTrimmed_CaseInsensitive_AndTruncatedAt100()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        // A 104-char name: 100 'a' followed by a 4-char tail. Its first 100
        // characters are a distinctive run that no other name contains.
        var longName = new string('a', 100) + "TAIL";
        await CreateProductAsync(member, tenantId, categoryId, longName, $"long-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Crimson Tee", $"crimson-{suffix}");

        using var anonymous = CreateClient();

        // (a) Leading/trailing whitespace is trimmed, and matching is case-insensitive.
        var trimmed = await anonymous.GetAsync($"/api/shop/{tenantId}/products?q={Uri.EscapeDataString("  crimson  ")}");
        var trimmedBody = await trimmed.Content.ReadAsStringAsync();
        var trimmedList = JsonSerializer.Deserialize<StorefrontProductListDto>(trimmedBody, Json)!;
        Assert.Single(trimmedList.Products);
        Assert.Equal("Crimson Tee", trimmedList.Products[0].Name);

        // (b) Uppercase query matches the mixed-case name.
        var upper = await anonymous.GetAsync($"/api/shop/{tenantId}/products?q=CRIMSON");
        var upperList = await ReadListAsync(upper);
        Assert.Single(upperList.Products);
        Assert.Equal("Crimson Tee", upperList.Products[0].Name);

        // (c) A 105-char query is truncated to its first 100 characters before
        // matching: the pattern "a"*100 matches the long name, whereas the
        // untruncated 105-char pattern (a*100 + "EXTRA") would match nothing.
        var longQuery = new string('a', 100) + "EXTRA";
        var (status, list) = await GetHttpProductsAsync(anonymous, tenantId, $"?q={Uri.EscapeDataString(longQuery)}");
        Assert.Equal(HttpStatusCode.OK, status);
        var names = list.Products.Select(p => p.Name).ToList();
        Assert.Contains(longName, names);
        Assert.DoesNotContain("Crimson Tee", names);
    }

    [Fact]
    public async Task AllProducts_InvalidSortValue_Returns400()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Any Tee", $"any-{suffix}");

        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=popularity");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"sort\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllProducts_CategorySlugFromAnotherTenant_ReturnsGeneric404()
    {
        var (tenantA, memberA, _) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var catA = await CreateCategoryAsync(memberA, tenantA, "Shirts", $"cat-a-{suffix}");
        await CreateProductAsync(memberA, tenantA, catA, "A Tee", $"a-{suffix}");
        await CreateCategoryAsync(memberB, tenantB, "Shirts", $"cat-b-{suffix}");

        using var anonymous = CreateClient();
        // B asks to filter by a category slug that only exists under A.
        var cross = await anonymous.GetAsync($"/api/shop/{tenantB}/products?categorySlug=cat-a-{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        // Same generic 404 as a slug that never existed.
        var never = await anonymous.GetAsync($"/api/shop/{tenantB}/products?categorySlug=never-{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, never.StatusCode);
    }

    [Fact]
    public async Task AllProducts_AllFourSorts_AreCorrect_AndTiesBreakById()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        // Card price = base price (each product's single variant has no
        // override and stock 1). Zebra and Apple share price 50 to test the tie.
        // Creation order: Mango, Zebra, Apple, Cherry — so TSID order (asc) is
        // Mango < Zebra < Apple < Cherry.
        await CreateProductAsync(member, tenantId, categoryId, "Mango Tee", $"mango-{suffix}", basePrice: 10);
        await CreateProductAsync(member, tenantId, categoryId, "Zebra Tee", $"zebra-{suffix}", basePrice: 50);
        await CreateProductAsync(member, tenantId, categoryId, "Apple Tee", $"apple-{suffix}", basePrice: 50);
        await CreateProductAsync(member, tenantId, categoryId, "Cherry Tee", $"cherry-{suffix}", basePrice: 90);

        using var anonymous = CreateClient();

        var newest = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=newest");
        var newestList = await ReadListAsync(newest);
        Assert.Equal(new[] { "Cherry Tee", "Apple Tee", "Zebra Tee", "Mango Tee" }, newestList.Products.Select(p => p.Name).ToArray());

        var priceAsc = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=price-asc");
        var priceAscList = await ReadListAsync(priceAsc);
        // Equal-price (50) tie broken by Id ascending: Zebra (created before Apple) first.
        Assert.Equal(new[] { "Mango Tee", "Zebra Tee", "Apple Tee", "Cherry Tee" }, priceAscList.Products.Select(p => p.Name).ToArray());

        var priceDesc = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=price-desc");
        var priceDescList = await ReadListAsync(priceDesc);
        Assert.Equal(new[] { "Cherry Tee", "Zebra Tee", "Apple Tee", "Mango Tee" }, priceDescList.Products.Select(p => p.Name).ToArray());

        var name = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=name");
        var nameList = await ReadListAsync(name);
        Assert.Equal(new[] { "Apple Tee", "Cherry Tee", "Mango Tee", "Zebra Tee" }, nameList.Products.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task AllProducts_SaleOnly_ReturnsOnlyProductsOnSale()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        await CreateProductAsync(member, tenantId, categoryId, "Sale One", $"saleone-{suffix}", basePrice: 100, compareAtPrice: 200);
        await CreateProductAsync(member, tenantId, categoryId, "Sale Two", $"saletwo-{suffix}", basePrice: 100, compareAtPrice: 150);
        await CreateProductAsync(member, tenantId, categoryId, "No Sale Higher", $"nohigh-{suffix}", basePrice: 100, compareAtPrice: 80);
        await CreateProductAsync(member, tenantId, categoryId, "No Compare", $"nocompare-{suffix}", basePrice: 100);

        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products?saleOnly=true&sort=name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response);
        Assert.Equal(2, list.Pagination.TotalCount);
        Assert.Equal(new[] { "Sale One", "Sale Two" }, list.Products.Select(p => p.Name).ToArray());
        Assert.All(list.Products, p => Assert.True(p.IsOnSale));

        // Without the flag, all four are returned.
        var all = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=name");
        var allList = await ReadListAsync(all);
        Assert.Equal(4, allList.Pagination.TotalCount);
    }

    [Fact]
    public async Task AllProducts_SoldOutProduct_IsMarkedSoldOut_AndOrderedLast()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        // Sold-out product created first (lower Id) and priced HIGHER, so under
        // price-desc it would sort first by price alone — proving the sold-out
        // rule is what pushes it last.
        await CreateProductAsync(member, tenantId, categoryId, "Sold Out Tee", $"soldout-{suffix}",
            variants: new object[] { new { color = "White", size = "M", sku = "S", stockQuantity = 0, priceOverride = (decimal?)null } },
            basePrice: 150);
        await CreateProductAsync(member, tenantId, categoryId, "In Stock Tee", $"instock-{suffix}", basePrice: 100);

        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=price-desc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response);

        Assert.Equal(new[] { "In Stock Tee", "Sold Out Tee" }, list.Products.Select(p => p.Name).ToArray());
        var soldOut = list.Products.Single(p => p.Name == "Sold Out Tee");
        Assert.True(soldOut.IsSoldOut);
        Assert.Equal(150m, soldOut.EffectivePrice); // falls back to BasePrice when no in-stock variant
        Assert.False(list.Products.Single(p => p.Name == "In Stock Tee").IsSoldOut);
    }

    [Fact]
    public async Task AllProducts_ThumbnailIsFirstOrderedImage_AndImageOrderDoesNotAffectProductOrder()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        var productId = await CreateProductAsync(member, tenantId, categoryId, "Featured Tee", $"featured-{suffix}");
        var otherProductId = await CreateProductAsync(member, tenantId, categoryId, "Other Tee", $"other-{suffix}");

        // Upload two images to "Featured Tee": first at order 0, second at order 1.
        var firstUpload = await UploadImageAsync(member, tenantId, productId, expectedGalleryVersion: 1, altText: "Front");
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);
        var secondUpload = await UploadImageAsync(member, tenantId, productId, expectedGalleryVersion: 2, altText: "Back");
        Assert.Equal(HttpStatusCode.Created, secondUpload.StatusCode);
        var gallery = await secondUpload.Content.ReadFromJsonAsync<StorefrontGalleryDto>();
        Assert.NotNull(gallery);
        // The upload response is ordered by DisplayOrder, proving the two images
        // really sit at order 0 and 1 (the thumbnail is the order-0 image).
        var firstImageId = gallery!.Images[0].Id;
        var secondImageId = gallery.Images[1].Id;
        Assert.Equal(0, gallery.Images[0].DisplayOrder);
        Assert.Equal(1, gallery.Images[1].DisplayOrder);

        // Give "Other Tee" one image so both products carry a thumbnail.
        await UploadImageAsync(member, tenantId, otherProductId, expectedGalleryVersion: 1, altText: "Sole");

        // By name: Featured Tee before Other Tee — independent of any image order.
        using var anonymous = CreateClient();
        var byName = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=name");
        var byNameList = await ReadListAsync(byName);
        Assert.Equal(new[] { "Featured Tee", "Other Tee" }, byNameList.Products.Select(p => p.Name).ToArray());
        var featured = byNameList.Products.Single(p => p.Name == "Featured Tee");
        Assert.Equal($"/api/shop/{tenantId}/media/{firstImageId}", featured.ThumbnailUrl);
        Assert.NotNull(featured.Images); // the summary still carries the full gallery
        Assert.Equal(2, featured.Images!.Count);

        // Reorder the gallery so the second image becomes order 0; the thumbnail
        // must follow the new order-0 image.
        var reorder = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/order", new
        {
            imageIds = new[] { secondImageId, firstImageId },
            expectedGalleryVersion = 3
        });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);

        var afterReorder = await anonymous.GetAsync($"/api/shop/{tenantId}/products?sort=name");
        var afterReorderList = await ReadListAsync(afterReorder);
        var featuredAfter = afterReorderList.Products.Single(p => p.Name == "Featured Tee");
        Assert.Equal($"/api/shop/{tenantId}/media/{secondImageId}", featuredAfter.ThumbnailUrl);
        Assert.Equal(2, afterReorderList.Products.Count);
    }

    [Fact]
    public async Task AllProducts_PaginationTotalReflectsFilteredList_NotWholeCatalog()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var categoryId = await CreateCategoryAsync(member, tenantId, "Shirts", $"cat-{suffix}");

        await CreateProductAsync(member, tenantId, categoryId, "One Apple", $"one-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Two Apple", $"two-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Three Banana", $"three-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Four Grape", $"four-{suffix}");
        await CreateProductAsync(member, tenantId, categoryId, "Five Cherry", $"five-{suffix}");

        using var anonymous = CreateClient();
        // q="apple" matches exactly two of the five; pageSize=1 → two pages.
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/products?q=apple&pageSize=1&pageNumber=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page1 = await ReadListAsync(response);
        Assert.Single(page1.Products);
        Assert.Equal(2, page1.Pagination.TotalCount);
        Assert.Equal(2, page1.Pagination.TotalPages);
        Assert.True(page1.Pagination.HasNextPage);

        var page2 = await anonymous.GetAsync($"/api/shop/{tenantId}/products?q=apple&pageSize=1&pageNumber=2");
        var page2List = await ReadListAsync(page2);
        Assert.Single(page2List.Products);
        Assert.False(page2List.Pagination.HasNextPage);
    }
}

internal sealed record StorefrontCategoryDto(string Id, string Name, string Slug, int DisplayOrder);

internal sealed record StorefrontCategoryListDto(IReadOnlyList<StorefrontCategoryDto> Categories);

internal sealed record StorefrontProductSummaryDto(
    string Id,
    string Name,
    string Slug,
    decimal EffectivePrice,
    decimal? CompareAtPrice,
    bool IsOnSale,
    bool IsSoldOut,
    string? ThumbnailUrl,
    IReadOnlyList<StorefrontImageDto>? Images = null);

internal sealed record StorefrontImageDto(string Id, string AltText, int DisplayOrder, int Width, int Height, string ContentUrl);

internal sealed record StorefrontGalleryDto(IReadOnlyList<StorefrontImageDto> Images, int GalleryVersion);

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
