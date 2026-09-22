using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using Npgsql;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B038's one-level category hierarchy: the nullable
/// ParentCategoryId column + self FK (Restrict), the parent-eligibility rule
/// (same tenant, active, no parent of its own, not self), the reparent guard
/// (a parent that already has children can never become a child), "effective
/// public activity" (a child is public only while it AND its root are active,
/// applied to the public list, product filtering, detail and media bytes),
/// the nested public category response, root-vs-child slug product
/// resolution, and the migration of pre-B038 flat rows into roots.
///
/// Data is authored through B026's authenticated admin API (a real tenant
/// owner's JWT) and read back through both the admin API (parentCategoryId
/// round-trip) and the anonymous storefront routes. Runs on a dedicated
/// database so its category counts never inflate other Shop test classes.
/// </summary>
[Collection(nameof(ShopCategoryHierarchyIsolatedCollection))]
public sealed class ShopCategoryHierarchyIntegrationTests(ShopCategoryHierarchyDbFixture db) : IDisposable
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

    private async Task<(string TenantId, HttpClient MemberClient, Tsid OwnerAccount)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var memberClient = CreateClient();
        SetMemberToken(memberClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, memberClient, ownerAccount);
    }

    private static async Task<string> CreateCategoryAsync(
        HttpClient memberClient, string tenantId, string name, string slug, int displayOrder = 1, string? parentCategoryId = null)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name,
            slug,
            displayOrder,
            parentCategoryId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task UpdateCategoryAsync(
        HttpClient memberClient, string tenantId, string categoryId, string name, string slug, int displayOrder, bool isActive, string? parentCategoryId)
    {
        var response = await memberClient.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{categoryId}", new
        {
            name,
            slug,
            displayOrder,
            isActive,
            parentCategoryId
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> CreateProductAsync(
        HttpClient memberClient, string tenantId, string categoryId, string name, string slug, decimal basePrice = 100)
    {
        var response = await memberClient.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "White", size = "M", sku = "P", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
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

    private static Task<HttpResponseMessage> UploadImageAsync(HttpClient client, string tenantId, string productId, int expectedGalleryVersion, string altText = "Front")
    {
        using var image = new Image<Rgba32>(32, 32, new Rgba32(30, 140, 90));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return client.PostAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images", BuildUploadForm(stream.ToArray(), altText, expectedGalleryVersion));
    }

    // ---- Spec scenario 1: root + direct child, create and edit each -------

    [Fact]
    public async Task RootAndDirectChild_CreatedAndEdited_RoundTripParentCategoryId()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootId = await CreateCategoryAsync(member, tenantId, "Outerwear", $"outerwear-{suffix}", displayOrder: 1);
        var childId = await CreateCategoryAsync(member, tenantId, "Jackets", $"jackets-{suffix}", displayOrder: 1, parentCategoryId: rootId);

        // The create response and the admin list both carry the new field:
        // the child points at its root, the root is null.
        var list = await member.GetAsync($"/api/tenants/{tenantId}/shop/categories");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadFromJsonAsync<CategoryHierarchyListDto>();
        Assert.NotNull(listBody);
        var rootRow = listBody!.Categories.Single(category => category.Id == rootId);
        var childRow = listBody.Categories.Single(category => category.Id == childId);
        Assert.Null(rootRow.ParentCategoryId);
        Assert.Equal(rootId, childRow.ParentCategoryId);

        // Edit each: rename + reorder the root (parent stays null), rename +
        // reorder the child (parent stays the root). Both persist.
        await UpdateCategoryAsync(member, tenantId, rootId, "Coats", $"coats-{suffix}", displayOrder: 2, isActive: true, parentCategoryId: null);
        await UpdateCategoryAsync(member, tenantId, childId, "Winter Jackets", $"winter-jackets-{suffix}", displayOrder: 3, isActive: true, parentCategoryId: rootId);

        var relist = await member.GetAsync($"/api/tenants/{tenantId}/shop/categories");
        var relistBody = await relist.Content.ReadFromJsonAsync<CategoryHierarchyListDto>();
        Assert.NotNull(relistBody);
        var updatedRoot = relistBody!.Categories.Single(category => category.Id == rootId);
        var updatedChild = relistBody.Categories.Single(category => category.Id == childId);
        Assert.Equal("Coats", updatedRoot.Name);
        Assert.Equal($"coats-{suffix}", updatedRoot.Slug);
        Assert.Equal(2, updatedRoot.DisplayOrder);
        Assert.Null(updatedRoot.ParentCategoryId);
        Assert.Equal("Winter Jackets", updatedChild.Name);
        Assert.Equal($"winter-jackets-{suffix}", updatedChild.Slug);
        Assert.Equal(3, updatedChild.DisplayOrder);
        Assert.Equal(rootId, updatedChild.ParentCategoryId);
    }

    // ---- Spec scenario 2: grandchild rejected -----------------------------

    [Fact]
    public async Task CreateCategory_UnderAChildCategory_IsRejectedAsTooDeep()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootId = await CreateCategoryAsync(member, tenantId, "Outerwear", $"outerwear-{suffix}");
        var childId = await CreateCategoryAsync(member, tenantId, "Jackets", $"jackets-{suffix}", parentCategoryId: rootId);

        var grandchild = await member.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Down Jackets",
            slug = $"down-{suffix}",
            displayOrder = 1,
            parentCategoryId = childId
        });

        Assert.Equal(HttpStatusCode.BadRequest, grandchild.StatusCode);
        using var document = JsonDocument.Parse(await grandchild.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("parentCategoryId", out var messages));
        Assert.Equal("Select an active root category.", messages[0].GetString());
    }

    // ---- Spec scenario 3: foreign-tenant parent rejected ------------------

    [Fact]
    public async Task CreateCategory_WithAnotherTenantsParent_IsRejected()
    {
        var (tenantA, memberA, _) = await NewTenantWithOwnerAsync();
        var (tenantB, memberB, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var foreignRoot = await CreateCategoryAsync(memberA, tenantA, "Shirts", $"shirts-a-{suffix}");

        var response = await memberB.PostAsJsonAsync($"/api/tenants/{tenantB}/shop/categories", new
        {
            name = "Tees",
            slug = $"tees-{suffix}",
            displayOrder = 1,
            parentCategoryId = foreignRoot
        });

        // The same non-leaking field error as any other invalid parent —
        // tenant B learns nothing about tenant A's catalog.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("parentCategoryId", out var messages));
        Assert.Equal("Select an active root category.", messages[0].GetString());
    }

    // ---- Spec scenario 4: self-parent rejected ----------------------------

    [Fact]
    public async Task UpdateCategory_ToItsOwnIdAsParent_IsRejected()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootId = await CreateCategoryAsync(member, tenantId, "Shirts", $"shirts-{suffix}");

        var response = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{rootId}", new
        {
            name = "Shirts",
            slug = $"shirts-{suffix}",
            displayOrder = 1,
            isActive = true,
            parentCategoryId = rootId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("parentCategoryId", out _));
    }

    // ---- Spec scenario 5: reparenting a parent-with-children is 409 -------

    [Fact]
    public async Task ReparentingACategoryThatHasChildren_IsRejectedWith409_AndNothingChanges()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootA = await CreateCategoryAsync(member, tenantId, "Root A", $"root-a-{suffix}", displayOrder: 1);
        var rootB = await CreateCategoryAsync(member, tenantId, "Root B", $"root-b-{suffix}", displayOrder: 2);
        var childOfA = await CreateCategoryAsync(member, tenantId, "Child Of A", $"child-a-{suffix}", displayOrder: 1, parentCategoryId: rootA);

        // rootA now has a child: moving it under rootB would create a third
        // level, so it must be a conflict.
        var reparent = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{rootA}", new
        {
            name = "Root A",
            slug = $"root-a-{suffix}",
            displayOrder = 1,
            isActive = true,
            parentCategoryId = rootB
        });
        Assert.Equal(HttpStatusCode.Conflict, reparent.StatusCode);
        Assert.Equal("application/problem+json", reparent.Content.Headers.ContentType!.MediaType);

        // (A second, related check: the self-parent rule also applies on the
        // update route — pointing a category at its own id is a 400 field
        // error, distinct from the 409 reparent conflict above.)
        var selfParent = await member.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{childOfA}", new
        {
            name = "Child Of A",
            slug = $"child-a-{suffix}",
            displayOrder = 1,
            isActive = true,
            parentCategoryId = childOfA
        });
        Assert.Equal(HttpStatusCode.BadRequest, selfParent.StatusCode);

        // Nothing moved: rootA is still a root, childOfA is still under it.
        var list = await member.GetAsync($"/api/tenants/{tenantId}/shop/categories");
        var body = await list.Content.ReadFromJsonAsync<CategoryHierarchyListDto>();
        Assert.NotNull(body);
        Assert.Null(body!.Categories.Single(category => category.Id == rootA).ParentCategoryId);
        Assert.Equal(rootA, body.Categories.Single(category => category.Id == childOfA).ParentCategoryId);
    }

    // ---- Spec scenario 6: deactivating a root hides its active child ------

    [Fact]
    public async Task DeactivatedRoot_KeepsChildStored_ButChildIsNoLongerPublic()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootId = await CreateCategoryAsync(member, tenantId, "Outerwear", $"outerwear-{suffix}");
        await CreateCategoryAsync(member, tenantId, "Jackets", $"jackets-{suffix}", displayOrder: 1, parentCategoryId: rootId);
        await CreateProductAsync(member, tenantId, rootId, "Root Tee", $"roottee-{suffix}");

        using var anonymous = CreateClient();

        // Both visible while active.
        var before = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        var beforeList = await before.Content.ReadFromJsonAsync<StorefrontCategoryHierarchyListDto>();
        Assert.NotNull(beforeList);
        Assert.Single(beforeList!.Categories);
        Assert.Single(beforeList.Categories[0].Children);

        // Deactivate the root through the admin API (allowed: children simply
        // remain stored).
        await UpdateCategoryAsync(member, tenantId, rootId, "Outerwear", $"outerwear-{suffix}", displayOrder: 1, isActive: false, parentCategoryId: null);

        // The child is still a real, admin-visible row...
        var adminList = await member.GetAsync($"/api/tenants/{tenantId}/shop/categories");
        var adminBody = await adminList.Content.ReadFromJsonAsync<CategoryHierarchyListDto>();
        Assert.NotNull(adminBody);
        Assert.Contains(adminBody!.Categories, category => category.Slug == $"jackets-{suffix}" && category.ParentCategoryId == rootId);

        // ...but the public list now has NO categories at all — a child is
        // public only while it AND its root are active.
        var after = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        var afterList = await after.Content.ReadFromJsonAsync<StorefrontCategoryHierarchyListDto>();
        Assert.NotNull(afterList);
        Assert.Empty(afterList!.Categories);

        // The child's product is gone from every public surface too.
        var childProducts = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/jackets-{suffix}/products");
        Assert.Equal(HttpStatusCode.NotFound, childProducts.StatusCode);
        var allProducts = await anonymous.GetAsync($"/api/shop/{tenantId}/products");
        var allList = await allProducts.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(allList);
        Assert.Empty(allList!.Products);
    }

    // ---- Spec scenario 7: nested public list, roots + ordered children ----

    [Fact]
    public async Task PublicCategoryList_ReturnsRootsWithChildrenNestedInDisplayOrder()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];

        // Two roots (order 1 and 3), one with two children deliberately
        // created out of order, one with none (Children must be []).
        var root1 = await CreateCategoryAsync(member, tenantId, "Apparel", $"apparel-{suffix}", displayOrder: 1);
        await CreateCategoryAsync(member, tenantId, "Pants", $"pants-{suffix}", displayOrder: 2, parentCategoryId: root1);
        await CreateCategoryAsync(member, tenantId, "Shirts", $"shirts-{suffix}", displayOrder: 1, parentCategoryId: root1);
        var root3 = await CreateCategoryAsync(member, tenantId, "Shoes", $"shoes-{suffix}", displayOrder: 3);

        using var anonymous = CreateClient();
        var response = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<StorefrontCategoryHierarchyListDto>();
        Assert.NotNull(list);

        // Only roots are top-level, in displayOrder order.
        Assert.Equal(2, list!.Categories.Count);
        Assert.Equal("Apparel", list.Categories[0].Name);
        Assert.Equal("Shoes", list.Categories[1].Name);
        Assert.Empty(list.Categories[1].Children);

        // The first root's children are ordered by their own displayOrder
        // (Shirts=1 before Pants=2), regardless of creation order.
        Assert.Equal(2, list.Categories[0].Children.Count);
        Assert.Equal("Shirts", list.Categories[0].Children[0].Name);
        Assert.Equal(1, list.Categories[0].Children[0].DisplayOrder);
        Assert.Equal("Pants", list.Categories[0].Children[1].Name);
        Assert.Equal(2, list.Categories[0].Children[1].DisplayOrder);
        // Children are leaves: their own Children is always an empty array.
        Assert.Empty(list.Categories[0].Children[0].Children);
    }

    // ---- Spec scenario 8: root slug = root + child products; child = own ---

    [Fact]
    public async Task ProductFiltering_RootSlugIncludesDirectChildProducts_ChildSlugOnlyItsOwn()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootSlug = $"root-{suffix}";
        var childSlug = $"child-{suffix}";
        var otherSlug = $"other-{suffix}";

        var rootId = await CreateCategoryAsync(member, tenantId, "Root", rootSlug, displayOrder: 1);
        var childId = await CreateCategoryAsync(member, tenantId, "Child", childSlug, displayOrder: 1, parentCategoryId: rootId);
        var otherId = await CreateCategoryAsync(member, tenantId, "Other", otherSlug, displayOrder: 2);

        await CreateProductAsync(member, tenantId, rootId, "Alpha Root Tee", $"alpha-{suffix}");
        await CreateProductAsync(member, tenantId, childId, "Bravo Child Tee", $"bravo-{suffix}");
        await CreateProductAsync(member, tenantId, otherId, "Charlie Other Tee", $"charlie-{suffix}");

        using var anonymous = CreateClient();

        // Root slug: the root's own product plus the direct child's product;
        // the unrelated root's product is excluded.
        var rootProducts = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/{rootSlug}/products");
        Assert.Equal(HttpStatusCode.OK, rootProducts.StatusCode);
        var rootList = await rootProducts.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(rootList);
        Assert.Equal(2, rootList!.Pagination.TotalCount);
        Assert.Equal(new[] { "Alpha Root Tee", "Bravo Child Tee" }, rootList.Products.Select(product => product.Name).ToArray());

        // Child slug: only that child's product.
        var childProducts = await anonymous.GetAsync($"/api/shop/{tenantId}/categories/{childSlug}/products");
        Assert.Equal(HttpStatusCode.OK, childProducts.StatusCode);
        var childList = await childProducts.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(childList);
        Assert.Single(childList!.Products);
        Assert.Equal("Bravo Child Tee", childList.Products[0].Name);

        // The same root/child semantics hold on the all-products route's
        // categorySlug filter (its base predicate is the shared effective
        // visibility rule).
        var allRoot = await anonymous.GetAsync($"/api/shop/{tenantId}/products?categorySlug={rootSlug}");
        var allRootList = await allRoot.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(allRootList);
        Assert.Equal(new[] { "Alpha Root Tee", "Bravo Child Tee" },
            allRootList!.Products.OrderBy(product => product.Name).Select(product => product.Name).ToArray());

        var allChild = await anonymous.GetAsync($"/api/shop/{tenantId}/products?categorySlug={childSlug}");
        var allChildList = await allChild.Content.ReadFromJsonAsync<StorefrontProductListDto>();
        Assert.NotNull(allChildList);
        Assert.Single(allChildList!.Products);
    }

    // ---- Spec scenario 9: pre-B038 flat rows migrate as roots --------------

    [Fact]
    public async Task PreB038FlatCategoryRows_MigrateAsRoots_WithUnchangedIdsAndSlugs()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var legacySlug = $"legacy-{suffix}";
        var legacyId = TsidId.NewId();
        var tenantTsid = TsidId.TryParse(tenantId, out var parsedTenant) ? parsedTenant : throw new InvalidOperationException();

        // Insert a row the way pre-B038 data looked: no parent at all (the
        // column does not even exist in that schema — here it is simply NULL,
        // exactly what the migration produces for every existing row).
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO shop_categories
                (id, tenant_id, name, slug, display_order, is_active, parent_category_id)
            VALUES (@id, @tenantId, 'Legacy Root', @slug, 1, true, NULL);
            """;
        command.Parameters.AddWithValue("id", legacyId.ToLong());
        command.Parameters.AddWithValue("tenantId", tenantTsid.ToLong());
        command.Parameters.AddWithValue("slug", legacySlug);
        await command.ExecuteNonQueryAsync();

        // The (already-migrated) admin API sees exactly this one category,
        // as a root (parentCategoryId null), with its original id and slug.
        var response = await member.GetAsync($"/api/tenants/{tenantId}/shop/categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<CategoryHierarchyListDto>();
        Assert.NotNull(list);
        Assert.Single(list!.Categories);
        Assert.Equal(TsidId.Format(legacyId), list.Categories[0].Id);
        Assert.Equal(legacySlug, list.Categories[0].Slug);
        Assert.Equal("Legacy Root", list.Categories[0].Name);
        Assert.Null(list.Categories[0].ParentCategoryId);

        // And the public list nests it as a root with no children.
        using var anonymous = CreateClient();
        var publicResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/categories");
        var publicList = await publicResponse.Content.ReadFromJsonAsync<StorefrontCategoryHierarchyListDto>();
        Assert.NotNull(publicList);
        Assert.Single(publicList!.Categories);
        Assert.Empty(publicList.Categories[0].Children);
    }

    // ---- Bonus (Spec step 7, media route): child media follows the root ----

    [Fact]
    public async Task ChildCategoryProductMedia_IsPublicOnlyWhileBothChildAndRootAreActive()
    {
        var (tenantId, member, _) = await NewTenantWithOwnerAsync();
        var suffix = Guid.NewGuid().ToString("N")[..14];
        var rootSlug = $"outerwear-{suffix}";
        var childSlug = $"jackets-{suffix}";
        var rootId = await CreateCategoryAsync(member, tenantId, "Outerwear", rootSlug, displayOrder: 1);
        var childId = await CreateCategoryAsync(member, tenantId, "Jackets", childSlug, displayOrder: 1, parentCategoryId: rootId);
        var productId = await CreateProductAsync(member, tenantId, childId, "Puffer Jacket", $"puffer-{suffix}");

        var upload = await UploadImageAsync(member, tenantId, productId, expectedGalleryVersion: 1);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var gallery = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = gallery.RootElement.GetProperty("images")[0].GetProperty("id").GetString()!;
        var mediaUrl = $"/api/shop/{tenantId}/media/{imageId}";

        using var anonymous = CreateClient();

        // Active root + active child: the bytes are public.
        var visible = await anonymous.GetAsync(mediaUrl);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);

        // Deactivate the root only: the child row and its product are still
        // active, but the media (and every other public surface) must stop.
        await UpdateCategoryAsync(member, tenantId, rootId, "Outerwear", rootSlug, displayOrder: 1, isActive: false, parentCategoryId: null);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(mediaUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/shop/{tenantId}/products/puffer-{suffix}")).StatusCode);
    }
}

internal sealed record CategoryHierarchyDto(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive,
    string? ParentCategoryId);

internal sealed record CategoryHierarchyListDto(IReadOnlyList<CategoryHierarchyDto> Categories, ShopPaginationDto Pagination);

internal sealed record StorefrontCategoryHierarchyDto(
    string Id,
    string Name,
    string Slug,
    int DisplayOrder,
    IReadOnlyList<StorefrontCategoryHierarchyDto> Children);

internal sealed record StorefrontCategoryHierarchyListDto(IReadOnlyList<StorefrontCategoryHierarchyDto> Categories);
