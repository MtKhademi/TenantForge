using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B026's tenant-scoped catalog admin surface: category
/// create/list/update, and the combined product + variants + size-guide
/// authoring payload (create, list, fetch-by-id, update-replaces). Proves the
/// security boundary end to end: the caller's JWT sub must hold an active
/// membership in the route's tenant (read straight from IAM's own tables via
/// Shop's raw SQL), so an unauthenticated caller gets 401 and a non-member
/// gets 403.
///
/// Runs on a dedicated database (ShopAdminIsolatedCollection) so its tenants,
/// categories and products never inflate other test classes' row counts.
/// </summary>
[Collection(nameof(ShopAdminIsolatedCollection))]
public sealed class ShopCatalogAdminIntegrationTests(ShopAdminDbFixture db) : IDisposable
{
    /// <summary>
    /// The minimal-API host serializes with the Web defaults (camelCase); the
    /// response DTOs must be read with the same contract or every property
    /// silently lands as default.
    /// </summary>
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

    /// <summary>
    /// A real, non-platform-admin JWT whose subject is the given account id —
    /// exactly the shape a signed-in tenant owner's token has.
    /// </summary>
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

    private static async Task<string> CreateCategoryAsync(HttpClient client, string tenantId, string name, string slug, int displayOrder = 1)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name,
            slug,
            displayOrder
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateProductAsync(HttpClient client, string tenantId, string categoryId, string name, string slug, object? variants = null, object? sizeGuideColumns = null, object? sizeGuideRows = null)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
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

    /// <summary>
    /// B035: a real, active account with a plain (non-owner) membership in
    /// the given tenant — no role assignment. Same direct-domain-entity
    /// seeding shape as CreateOwnerAccountAsync, but through IAM's own
    /// IamDbContext (the Shop fixtures' CreateContext) because membership
    /// rows are IAM's own tables.
    /// </summary>
    private async Task<Tsid> CreateMemberAccountAsync(string tenantId, string email)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId);
        if (tenantTsid is null)
        {
            throw new InvalidOperationException("tenantId must be a canonical TSID string.");
        }

        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid.Value, account.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return account.Id;
    }

    /// <summary>
    /// B035: assigns a tenant role carrying exactly the given permission
    /// keys to a membership, through IAM's own domain entities.
    /// </summary>
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

    [Fact]
    public async Task Category_CreateListUpdate_RoundTripsForTheTenantOwner()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var slug = $"shirts-{Guid.NewGuid():N}"[..14];

        // Create: 201, Location header, trimmed name, normalized (lower-case) slug, active by default.
        var createResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "  Shirts  ",
            slug = slug.ToUpperInvariant(),
            displayOrder = 3
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using (var document = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()))
        {
            var created = document.RootElement;
            var categoryId = created.GetProperty("id").GetString()!;
            Assert.True(TsidId.TryParse(categoryId, out _), "id must be a canonical TSID string");
            Assert.Equal(tenantId, created.GetProperty("tenantId").GetString());
            Assert.Equal("Shirts", created.GetProperty("name").GetString());
            Assert.Equal(slug, created.GetProperty("slug").GetString());
            Assert.Equal(3, created.GetProperty("displayOrder").GetInt32());
            Assert.True(created.GetProperty("isActive").GetBoolean());
            Assert.EndsWith($"/api/tenants/{tenantId}/shop/categories/{categoryId}", createResponse.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        // List: tenant-scoped, paginated, ordered by displayOrder then id.
        var listResponse = await client.GetAsync($"/api/tenants/{tenantId}/shop/categories?pageNumber=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<CategoryListDto>();
        Assert.NotNull(list);
        Assert.Equal(1, list!.Pagination.TotalCount);
        Assert.Equal(1, list.Pagination.TotalPages);
        Assert.False(list.Pagination.HasPreviousPage);
        Assert.False(list.Pagination.HasNextPage);
        var listed = list.Categories.Single();
        Assert.Equal("Shirts", listed.Name);

        // Update: rename, re-slug, reorder, deactivate — all reflected in the response.
        var updatedSlug = $"shirts-updated-{Guid.NewGuid():N}"[..20];
        var updateResponse = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/categories/{listed.Id}", new
        {
            name = "Dress Shirts",
            slug = updatedSlug,
            displayOrder = 5,
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(updated);
        Assert.Equal("Dress Shirts", updated!.Name);
        Assert.Equal(updatedSlug, updated.Slug);
        Assert.Equal(5, updated.DisplayOrder);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Category_MissingNameOrSlug_Returns400ValidationProblem()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "   ",
            slug = (string?)null,
            displayOrder = 1
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("name", out _));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task Product_CreateWithVariantsAndSizeGuide_PersistsEveryRow_AndFetchByIdReturnsTheSameShape()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shirts", $"cat-{Guid.NewGuid():N}"[..16]);

        var createResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Classic Shirt",
            slug = $"classic-shirt-{Guid.NewGuid():N}"[..22],
            description = "A cotton classic",
            categoryId,
            basePrice = 890000,
            compareAtPrice = (decimal?)null,
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

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createdBody = await createResponse.Content.ReadAsStringAsync();
        // JsonSerializerDefaults.Web so camelCase matches what the minimal-API
        // host serializes; ReadFromJsonAsync in the other assertions does the
        // same thing.
        var created = JsonSerializer.Deserialize<ProductDto>(createdBody, Json);
        Assert.NotNull(created);

        Assert.Equal(tenantId, created!.TenantId);
        Assert.Equal(categoryId, created.CategoryId);
        Assert.Equal("A cotton classic", created.Description);
        Assert.Equal(890000m, created.BasePrice);
        Assert.Null(created.CompareAtPrice);
        Assert.True(created.IsActive);

        // Variants come back sorted by color then size ("L" < "M"), ids are
        // TSID strings.
        var variants = created.Variants;
        Assert.Equal(2, variants.Count);
        Assert.Equal("L", variants[0].Size);
        Assert.Equal("SHIRT-WHT-L", variants[0].Sku);
        Assert.Equal(5, variants[0].StockQuantity);
        Assert.Equal(120000m, variants[0].PriceOverride);
        Assert.Equal("M", variants[1].Size);
        Assert.Equal(10, variants[1].StockQuantity);
        Assert.Null(variants[1].PriceOverride);
        foreach (var variant in variants)
        {
            Assert.True(TsidId.TryParse(variant.Id, out _), "variant id must be a canonical TSID string");
        }

        // The size guide is a real table set: two named columns in order, one
        // cell per (row, column) pair carrying exactly the submitted value.
        Assert.Equal(2, created.SizeGuideColumns.Count);
        Assert.Equal("chest", created.SizeGuideColumns[0].Name);
        Assert.Equal(0, created.SizeGuideColumns[0].DisplayOrder);
        Assert.Equal("waist", created.SizeGuideColumns[1].Name);
        Assert.Equal(1, created.SizeGuideColumns[1].DisplayOrder);
        var chestId = created.SizeGuideColumns[0].Id;
        var waistId = created.SizeGuideColumns[1].Id;

        Assert.Equal(2, created.SizeGuideRows.Count);
        var mRow = created.SizeGuideRows.Single(row => row.SizeLabel == "M");
        Assert.Equal(0, mRow.DisplayOrder);
        Assert.Equal(2, mRow.Cells.Count);
        Assert.Contains(new SizeGuideCellDto(chestId, "96"), mRow.Cells);
        Assert.Contains(new SizeGuideCellDto(waistId, "80"), mRow.Cells);
        var lRow = created.SizeGuideRows.Single(row => row.SizeLabel == "L");
        Assert.Contains(new SizeGuideCellDto(chestId, "102"), lRow.Cells);
        Assert.Contains(new SizeGuideCellDto(waistId, "86"), lRow.Cells);

        // Fetching by id returns an identical body (same shape, same data).
        var fetchResponse = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetchResponse.StatusCode);
        Assert.Equal(createdBody, await fetchResponse.Content.ReadAsStringAsync());

        // The paginated list carries the product as a summary with its live variant count.
        var listResponse = await client.GetAsync($"/api/tenants/{tenantId}/shop/products");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ProductListDto>();
        Assert.NotNull(list);
        var summary = list!.Products.Single();
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(2, summary.VariantCount);
        Assert.Equal(890000m, summary.BasePrice);
    }

    [Fact]
    public async Task Product_Update_ReplacesVariantsAndSizeGuideWholesale()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shirts", $"cat-{Guid.NewGuid():N}"[..16]);

        var createResponse = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Updated Shirt",
            slug = $"update-shirt-{Guid.NewGuid():N}"[..22],
            description = "Before update",
            categoryId,
            basePrice = 100000,
            compareAtPrice = (decimal?)null,
            variants = new object[]
            {
                new { color = "White", size = "M", sku = "OLD-WHT-M", stockQuantity = 1, priceOverride = (decimal?)null },
                new { color = "White", size = "L", sku = "OLD-WHT-L", stockQuantity = 2, priceOverride = (decimal?)null }
            },
            sizeGuideColumns = new[] { "chest" },
            sizeGuideRows = new object[]
            {
                new { sizeLabel = "M", values = new[] { "90" } }
            }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);
        var oldVariantIds = created!.Variants.Select(variant => variant.Id).ToList();
        Assert.Equal(2, oldVariantIds.Count);

        var updateResponse = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{created.Id}", new
        {
            name = "Updated Shirt v2",
            slug = created.Slug,
            description = "After update",
            categoryId,
            basePrice = 150000,
            compareAtPrice = 200000,
            isActive = true,
            variants = new object[]
            {
                new { color = "Navy", size = "S", sku = "NEW-NAV-S", stockQuantity = 7, priceOverride = (decimal?)null }
            },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Shirt v2", updated!.Name);
        Assert.Equal(150000m, updated.BasePrice);
        Assert.Equal(200000m, updated.CompareAtPrice);
        Assert.Equal("After update", updated.Description);

        // Exactly one variant now — the new one; both old variant ids are gone,
        // and the whole size-guide table was removed too.
        Assert.Single(updated.Variants);
        Assert.Equal("NEW-NAV-S", updated.Variants[0].Sku);
        Assert.Equal(7, updated.Variants[0].StockQuantity);
        Assert.DoesNotContain(updated.Variants[0].Id, oldVariantIds);
        Assert.Empty(updated.SizeGuideColumns);
        Assert.Empty(updated.SizeGuideRows);
    }

    [Fact]
    public async Task DuplicateSlug_WithinOneTenant_IsRejected_ButAllowedAcrossTenants()
    {
        var admin = await PlatformAdminClientAsync();

        var ownerA = await CreateOwnerAccountAsync($"ownerA-{Guid.NewGuid():N}@tenantforge.local");
        var tenantA = await CreateTenantWithOwnerAsync(admin, ownerA, "Boutique A");
        using var clientA = CreateClient();
        SetMemberToken(clientA, ownerA, $"ownerA-{ownerA.ToLong():x}@tenantforge.local");

        var sharedSlug = $"shared-{Guid.NewGuid():N}"[..16];
        var firstCategory = await CreateCategoryAsync(clientA, tenantA, "First", sharedSlug);
        Assert.NotNull(firstCategory);

        // Same category slug, same tenant -> 409 problem.
        var duplicateCategory = await clientA.PostAsJsonAsync($"/api/tenants/{tenantA}/shop/categories", new
        {
            name = "Second",
            slug = sharedSlug.ToUpperInvariant(),
            displayOrder = 2
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateCategory.StatusCode);
        Assert.Equal("application/problem+json", duplicateCategory.Content.Headers.ContentType!.MediaType);
        var categoryProblem = await duplicateCategory.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate category slug", categoryProblem);

        // Same product slug, same tenant -> 400 validation problem.
        var productA = await CreateProductAsync(clientA, tenantA, firstCategory, "Product A", sharedSlug);
        var duplicateProduct = await clientA.PostAsJsonAsync($"/api/tenants/{tenantA}/shop/products", new
        {
            name = "Another Product",
            slug = sharedSlug,
            description = (string?)null,
            categoryId = firstCategory,
            basePrice = 100,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "White", size = "M", sku = "X", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateProduct.StatusCode);
        using (var document = JsonDocument.Parse(await duplicateProduct.Content.ReadAsStringAsync()))
        {
            Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("slug", out _));
        }

        // Updating a product to keep its own slug is NOT a conflict.
        var keepOwnSlug = await clientA.PutAsJsonAsync($"/api/tenants/{tenantA}/shop/products/{productA}", new
        {
            name = "Another Product",
            slug = sharedSlug,
            description = (string?)null,
            categoryId = firstCategory,
            basePrice = 100,
            compareAtPrice = (decimal?)null,
            isActive = true,
            variants = new object[] { new { color = "White", size = "M", sku = "X", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.OK, keepOwnSlug.StatusCode);

        // The very same slug in a different tenant is fine for both.
        var ownerB = await CreateOwnerAccountAsync($"ownerB-{Guid.NewGuid():N}@tenantforge.local");
        var tenantB = await CreateTenantWithOwnerAsync(admin, ownerB, "Boutique B");
        using var clientB = CreateClient();
        SetMemberToken(clientB, ownerB, $"ownerB-{ownerB.ToLong():x}@tenantforge.local");
        var categoryB = await CreateCategoryAsync(clientB, tenantB, "First B", sharedSlug);
        Assert.NotNull(categoryB);
        Assert.NotEqual(firstCategory, categoryB);
        await CreateProductAsync(clientB, tenantB, categoryB, "Product B", sharedSlug);
    }

    [Fact]
    public async Task SizeGuideRowWithTheWrongValueCount_IsRejectedAndPersistsNothing()
    {
        var (tenantId, client, _) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shirts", $"cat-{Guid.NewGuid():N}"[..16]);

        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Broken Guide",
            slug = $"broken-{Guid.NewGuid():N}"[..16],
            description = (string?)null,
            categoryId,
            basePrice = 500,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "White", size = "M", sku = "B", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = new[] { "chest", "waist" },
            sizeGuideRows = new object[]
            {
                new { sizeLabel = "M", values = new[] { "96", "80", "too many" } }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("sizeGuideRows", out _));

        var listResponse = await client.GetAsync($"/api/tenants/{tenantId}/shop/products");
        var list = await listResponse.Content.ReadFromJsonAsync<ProductListDto>();
        Assert.NotNull(list);
        Assert.Empty(list!.Products);
    }

    [Fact]
    public async Task UnauthenticatedCaller_Gets401_OnEveryCatalogRoute()
    {
        var (tenantId, _, _) = await NewTenantWithOwnerAsync();
        using var client = CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/tenants/{tenantId}/shop/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Nope",
            slug = "nope",
            displayOrder = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/tenants/{tenantId}/shop/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name = "Nope",
            slug = "nope",
            description = (string?)null,
            categoryId = TsidId.Format(TsidId.NewId()),
            basePrice = 1,
            compareAtPrice = (decimal?)null,
            variants = (object?)null,
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        })).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedNonMember_Gets403_OnEveryCatalogRoute()
    {
        var (tenantId, _, ownerAccount) = await NewTenantWithOwnerAsync();

        // A real, active account that simply has no membership in this tenant.
        var outsider = await CreateOwnerAccountAsync($"outsider-{Guid.NewGuid():N}@tenantforge.local");
        using var client = CreateClient();
        SetMemberToken(client, outsider, $"outsider-{outsider.ToLong():x}@tenantforge.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/tenants/{tenantId}/shop/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Intruder",
            slug = "intruder",
            displayOrder = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/tenants/{tenantId}/shop/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{TsidId.Format(TsidId.NewId())}", new
        {
            name = "Intruder",
            slug = "intruder",
            description = (string?)null,
            categoryId = TsidId.Format(TsidId.NewId()),
            basePrice = 1,
            compareAtPrice = (decimal?)null,
            isActive = true,
            variants = (object?)null,
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        })).StatusCode);

        // A route tenantId that is not a canonical TSID is denied the same way
        // (403, never 400 or 500) even by a member token.
        using var memberClient = CreateClient();
        SetMemberToken(memberClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync("/api/tenants/not-a-tsid/shop/categories")).StatusCode);
    }

    // B035: a tenant Owner succeeds on every mutating catalog endpoint with no
    // role grant at all (Owner bypass) — that is exactly what the Owner-token
    // happy-path tests above already prove end to end.

    [Fact]
    public async Task MemberWithoutAShopRoleGrant_Gets403_OnTheMutatingCatalogRoutes()
    {
        var (tenantId, _, _) = await NewTenantWithOwnerAsync();
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        using var client = CreateClient();
        SetMemberToken(client, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        // POST create category: 403, even though this IS an active member.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name = "Shirts",
            slug = "shirts",
            displayOrder = 1
        })).StatusCode);

        // PUT update product: 403 as well (same Shop.Catalog.Manage key).
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{TsidId.Format(TsidId.NewId())}", new
        {
            name = "Nope",
            slug = "nope",
            description = (string?)null,
            categoryId = TsidId.Format(TsidId.NewId()),
            basePrice = 1,
            compareAtPrice = (decimal?)null,
            isActive = true,
            variants = (object?)null,
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        })).StatusCode);

        // Read-only routes stay membership-only (unchanged): the same member
        // can still see the tenant's catalog.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{tenantId}/shop/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tenants/{tenantId}/shop/products")).StatusCode);
    }

    [Fact]
    public async Task MemberWithARoleGrantingShopCatalogManage_CanCreateAndUpdate()
    {
        var (tenantId, _, _) = await NewTenantWithOwnerAsync();
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        await GrantRoleAsync(tenantId, memberAccount, ["Shop.Catalog.Manage"]);
        using var client = CreateClient();
        SetMemberToken(client, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        // The granted key unlocks the mutating category/product endpoints.
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shirts", $"shirts-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Classic Shirt", $"classic-{Guid.NewGuid():N}"[..16]);

        var updateResponse = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}", new
        {
            name = "Classic Shirt v2",
            slug = $"classic-v2-{Guid.NewGuid():N}"[..20],
            description = (string?)null,
            categoryId,
            basePrice = 120,
            compareAtPrice = (decimal?)null,
            isActive = true,
            variants = new object[] { new { color = "Navy", size = "M", sku = "V2", stockQuantity = 3, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }
}

internal sealed record CategoryDto(string Id, string TenantId, string Name, string Slug, int DisplayOrder, bool IsActive);

internal sealed record CategoryListDto(IReadOnlyList<CategoryDto> Categories, ShopPaginationDto Pagination);

internal sealed record ShopPaginationDto(int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage);

internal sealed record ProductVariantDto(string Id, string Color, string Size, string Sku, int StockQuantity, decimal? PriceOverride);

internal sealed record SizeGuideColumnDto(string Id, string Name, int DisplayOrder);

internal sealed record SizeGuideCellDto(string ColumnId, string Value);

internal sealed record SizeGuideRowDto(string Id, string SizeLabel, int DisplayOrder, IReadOnlyList<SizeGuideCellDto> Cells);

internal sealed record ProductDto(
    string Id,
    string TenantId,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ProductVariantDto> Variants,
    IReadOnlyList<SizeGuideColumnDto> SizeGuideColumns,
    IReadOnlyList<SizeGuideRowDto> SizeGuideRows);

internal sealed record ProductSummaryDto(string Id, string Name, string Slug, string CategoryId, decimal BasePrice, bool IsActive, int VariantCount);

internal sealed record ProductListDto(IReadOnlyList<ProductSummaryDto> Products, ShopPaginationDto Pagination);
