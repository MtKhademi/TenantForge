using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Iam.Domain;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects B036's product-gallery endpoints: authenticated upload/reorder/
/// delete with a transactional GalleryVersion guard and an eight-image cap;
/// real decode-and-reencode validation (never trusting extension or
/// Content-Type); a protected byte route for draft previews; and a public
/// byte route that only ever serves an active product's image in an active
/// category, for an active-only, same-tenant match.
///
/// Every fact authors catalog data through B026's authenticated admin API
/// (a real tenant owner's JWT), then drives the media endpoints with a
/// multipart upload built from a real, in-memory-generated JPEG/PNG image
/// (SixLabors.ImageSharp — the same library the module uses to validate and
/// re-encode).
///
/// Runs on a dedicated database (ShopProductMediaIsolatedCollection) so its
/// tenants/products/images/galleries never inflate any other Shop database's
/// row counts or filesystem side effects.
/// </summary>
[Collection(nameof(ShopProductMediaIsolatedCollection))]
public sealed class ShopProductMediaIntegrationTests(ShopProductMediaDbFixture db) : IDisposable
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

    private async Task<(string TenantId, HttpClient OwnerClient)> NewTenantWithOwnerAsync()
    {
        var admin = await PlatformAdminClientAsync();
        var ownerAccount = await CreateOwnerAccountAsync($"owner-{Guid.NewGuid():N}@tenantforge.local");
        var tenantId = await CreateTenantWithOwnerAsync(admin, ownerAccount, $"Boutique {Guid.NewGuid():N}"[..8]);
        var ownerClient = CreateClient();
        SetMemberToken(ownerClient, ownerAccount, $"owner-{ownerAccount.ToLong():x}@tenantforge.local");
        return (tenantId, ownerClient);
    }

    private async Task<Tsid> CreateMemberAccountAsync(string tenantId, string email)
    {
        var tenantTsid = TsidId.TryParseNullable(tenantId)!.Value;
        await using var context = db.CreateContext();
        var account = Account.CreateUser(email, "Shop Member", "already-hashed-for-test", DateTimeOffset.UtcNow);
        context.Accounts.Add(account);
        context.TenantMemberships.Add(TenantMembership.CreateMember(tenantTsid, account.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<string> CreateCategoryAsync(HttpClient client, string tenantId, string name, string slug)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/categories", new
        {
            name,
            slug,
            displayOrder = 1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateProductAsync(HttpClient client, string tenantId, string categoryId, string name, string slug, bool isActive = true)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/shop/products", new
        {
            name,
            slug,
            description = (string?)null,
            categoryId,
            basePrice = 100,
            compareAtPrice = (decimal?)null,
            variants = new object[] { new { color = "White", size = "M", sku = "P", stockQuantity = 1, priceOverride = (decimal?)null } },
            sizeGuideColumns = (object?)null,
            sizeGuideRows = (object?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productId = document.RootElement.GetProperty("id").GetString()!;

        if (!isActive)
        {
            var update = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}", new
            {
                name,
                slug,
                description = (string?)null,
                categoryId,
                basePrice = 100,
                compareAtPrice = (decimal?)null,
                isActive = false,
                variants = new object[] { new { color = "White", size = "M", sku = "P", stockQuantity = 1, priceOverride = (decimal?)null } },
                sizeGuideColumns = (object?)null,
                sizeGuideRows = (object?)null
            });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        }

        return productId;
    }

    // ---- Real, in-memory-generated image fixtures ----------------------

    /// <summary>A minimal, valid opaque JPEG of the given size.</summary>
    private static byte[] MakeJpeg(int width = 32, int height = 32)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 40, 200));
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    /// <summary>A minimal, valid opaque PNG — used to prove real-content sniffing (not extension).</summary>
    private static byte[] MakePng(int width = 32, int height = 32)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 200, 40));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>A valid JPEG carrying an EXIF profile with a GPS tag set, to prove stripping.</summary>
    private static byte[] MakeJpegWithExifGps()
    {
        using var image = new Image<Rgba32>(32, 32, new Rgba32(80, 80, 80));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "TenantForgeTestCamera");
        image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitude, new SixLabors.ImageSharp.Rational[]
        {
            new(35, 1), new(41, 1), new(0, 1)
        });
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream, new JpegEncoder());
        return stream.ToArray();
    }

    private static MultipartFormDataContent BuildUploadForm(byte[] bytes, string fileName, string contentType, string? altText, int expectedGalleryVersion)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "File", fileName);
        if (altText is not null)
        {
            form.Add(new StringContent(altText), "AltText");
        }
        form.Add(new StringContent(expectedGalleryVersion.ToString()), "ExpectedGalleryVersion");
        return form;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string tenantId, string productId, byte[] bytes, int expectedGalleryVersion, string fileName = "photo.jpg", string contentType = "image/jpeg", string? altText = "Front view") =>
        client.PostAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images", BuildUploadForm(bytes, fileName, contentType, altText, expectedGalleryVersion));

    private sealed record ImageDto(string Id, string AltText, int DisplayOrder, int Width, int Height, string ContentUrl);

    private sealed record GalleryDto(List<ImageDto> Images, int GalleryVersion);

    // ---- Scenarios -------------------------------------------------------

    [Fact]
    public async Task UploadReorderDelete_RoundTripsCorrectly()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shirts", $"shirts-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Classic Shirt", $"classic-{Guid.NewGuid():N}"[..16]);

        // Upload two images. Each 201 carries a fresh gallery with the right count/order.
        var firstUpload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1, altText: "Front");
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);
        var afterFirst = await firstUpload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        Assert.NotNull(afterFirst);
        Assert.Single(afterFirst!.Images);
        Assert.Equal(2, afterFirst.GalleryVersion);
        Assert.Equal("Front", afterFirst.Images[0].AltText);
        Assert.Equal(0, afterFirst.Images[0].DisplayOrder);
        Assert.True(TsidId.TryParse(afterFirst.Images[0].Id, out _), "image id must be a canonical TSID string");

        var secondUpload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 2, altText: "Back");
        Assert.Equal(HttpStatusCode.Created, secondUpload.StatusCode);
        var afterSecond = await secondUpload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        Assert.NotNull(afterSecond);
        Assert.Equal(2, afterSecond!.Images.Count);
        Assert.Equal(3, afterSecond.GalleryVersion);
        var firstId = afterSecond.Images[0].Id;
        var secondId = afterSecond.Images[1].Id;

        // Reorder: swap the two images.
        var reorder = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/order", new
        {
            imageIds = new[] { secondId, firstId },
            expectedGalleryVersion = 3
        });
        Assert.Equal(HttpStatusCode.OK, reorder.StatusCode);
        var afterReorder = await reorder.Content.ReadFromJsonAsync<GalleryDto>(Json);
        Assert.NotNull(afterReorder);
        Assert.Equal(secondId, afterReorder!.Images[0].Id);
        Assert.Equal(0, afterReorder.Images[0].DisplayOrder);
        Assert.Equal(firstId, afterReorder.Images[1].Id);
        Assert.Equal(1, afterReorder.Images[1].DisplayOrder);
        Assert.Equal(4, afterReorder.GalleryVersion);

        // Delete the now-first image (originally the second upload); the
        // remaining image is renumbered to display order 0.
        var delete = await client.DeleteAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/{secondId}?expectedGalleryVersion=4");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var productAfterDelete = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        Assert.Equal(HttpStatusCode.OK, productAfterDelete.StatusCode);
        using var productBody = JsonDocument.Parse(await productAfterDelete.Content.ReadAsStringAsync());
        var images = productBody.RootElement.GetProperty("images");
        Assert.Equal(1, images.GetArrayLength());
        Assert.Equal(firstId, images[0].GetProperty("id").GetString());
        Assert.Equal(0, images[0].GetProperty("displayOrder").GetInt32());
        Assert.Equal(5, productBody.RootElement.GetProperty("galleryVersion").GetInt32());
    }

    [Fact]
    public async Task NinthImage_OnAFullGallery_IsRejected()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Hats", $"hats-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Cap", $"cap-{Guid.NewGuid():N}"[..16]);

        var version = 1;
        for (var i = 0; i < 8; i++)
        {
            var response = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: version);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            version++;
        }

        var ninth = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: version);
        Assert.Equal(HttpStatusCode.BadRequest, ninth.StatusCode);
        using var document = JsonDocument.Parse(await ninth.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("file", out _));

        // The gallery is unchanged: still exactly eight images, same version.
        var product = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        using var productBody = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        Assert.Equal(8, productBody.RootElement.GetProperty("images").GetArrayLength());
        Assert.Equal(version, productBody.RootElement.GetProperty("galleryVersion").GetInt32());
    }

    [Fact]
    public async Task StaleExpectedGalleryVersion_IsRejectedAsAConflict()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Belts", $"belts-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Leather Belt", $"belt-{Guid.NewGuid():N}"[..16]);

        // The real current version is 1; send a stale (already-advanced) one.
        var response = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 99);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        // Nothing was persisted.
        var product = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        using var productBody = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        Assert.Equal(0, productBody.RootElement.GetProperty("images").GetArrayLength());
        Assert.Equal(1, productBody.RootElement.GetProperty("galleryVersion").GetInt32());
    }

    [Fact]
    public async Task FileWithAFakeMismatchedMimeType_IsRejected()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Bags", $"bags-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Tote Bag", $"tote-{Guid.NewGuid():N}"[..16]);

        // Plain text bytes, dressed up with an image/jpeg Content-Type and a
        // .jpg extension — the validator must decode the real bytes, not
        // trust either signal.
        var fakeBytes = System.Text.Encoding.UTF8.GetBytes("this is not an image, just text pretending to be one");
        var response = await UploadAsync(client, tenantId, productId, fakeBytes, expectedGalleryVersion: 1, fileName: "fake.jpg", contentType: "image/jpeg");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SvgFile_IsRejected()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Posters", $"posters-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Poster", $"poster-{Guid.NewGuid():N}"[..16]);

        var svgBytes = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");
        var response = await UploadAsync(client, tenantId, productId, svgBytes, expectedGalleryVersion: 1, fileName: "logo.svg", contentType: "image/svg+xml");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task CorruptImageFile_IsRejected()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Shoes", $"shoes-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Sneaker", $"sneaker-{Guid.NewGuid():N}"[..16]);

        // A real JPEG signature followed by garbage — DetectFormat succeeds
        // (it reads only the header), but the pixel decode must fail cleanly.
        var real = MakeJpeg();
        var truncated = real[..Math.Min(64, real.Length)];
        var response = await UploadAsync(client, tenantId, productId, truncated, expectedGalleryVersion: 1, fileName: "broken.jpg", contentType: "image/jpeg");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task OversizedImage_IsRejected()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Rugs", $"rugs-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Rug", $"rug-{Guid.NewGuid():N}"[..16]);

        // 4200x4200 exceeds the 4096px-per-dimension limit; a solid color
        // JPEG at this size is still comfortably under 5 MiB, isolating the
        // dimension check from the byte-size check.
        var oversized = MakeJpeg(4200, 4200);
        var response = await UploadAsync(client, tenantId, productId, oversized, expectedGalleryVersion: 1, fileName: "huge.jpg", contentType: "image/jpeg");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PathTraversalStyleFileName_IsHarmless()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Watches", $"watches-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Watch", $"watch-{Guid.NewGuid():N}"[..16]);

        var response = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1, fileName: "../../../etc/passwd.jpg");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // The server generates its own random StorageKey — the submitted
        // (malicious) file name never reaches the filesystem path. Proven by
        // the fact upload succeeded normally and the file is servable back.
        var gallery = await response.Content.ReadFromJsonAsync<GalleryDto>(Json);
        Assert.NotNull(gallery);
        var contentResponse = await client.GetAsync(gallery!.Images[0].ContentUrl);
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);

        // No file escaped the configured media root onto an unrelated path.
        Assert.False(File.Exists("/etc/passwd.jpg"));
    }

    [Fact]
    public async Task ImageWithExifGpsMetadata_IsStrippedWhenStoredAndServed()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Cameras", $"cameras-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Camera Strap", $"strap-{Guid.NewGuid():N}"[..16]);

        var withExif = MakeJpegWithExifGps();
        var upload = await UploadAsync(client, tenantId, productId, withExif, expectedGalleryVersion: 1);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var gallery = await upload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        Assert.NotNull(gallery);

        var contentResponse = await client.GetAsync(gallery!.Images[0].ContentUrl);
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        var servedBytes = await contentResponse.Content.ReadAsByteArrayAsync();

        using var servedImage = Image.Load(servedBytes);
        Assert.Null(servedImage.Metadata.ExifProfile);
        Assert.Null(servedImage.Metadata.IccProfile);
        Assert.Null(servedImage.Metadata.XmpProfile);
    }

    [Fact]
    public async Task ForeignTenant_IsDeniedOnEveryMediaOperation()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Jackets", $"jackets-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Jacket", $"jacket-{Guid.NewGuid():N}"[..16]);
        var upload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);
        var gallery = await upload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        var imageId = gallery!.Images[0].Id;

        // A second, unrelated tenant + owner.
        var admin = await PlatformAdminClientAsync();
        var foreignOwner = await CreateOwnerAccountAsync($"foreign-{Guid.NewGuid():N}@tenantforge.local");
        var foreignTenantId = await CreateTenantWithOwnerAsync(admin, foreignOwner, "Foreign Boutique");
        using var foreignClient = CreateClient();
        SetMemberToken(foreignClient, foreignOwner, $"foreign-{foreignOwner.ToLong():x}@tenantforge.local");

        // The foreign owner tries to act against the FIRST tenant's route
        // (their own tenant segment does not contain this product), so every
        // one of these must be denied — never a leak of the other tenant's
        // gallery state.
        var foreignUpload = await UploadAsync(foreignClient, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 2);
        Assert.Equal(HttpStatusCode.Forbidden, foreignUpload.StatusCode);

        var foreignReorder = await foreignClient.PutAsJsonAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/order", new
        {
            imageIds = new[] { imageId },
            expectedGalleryVersion = 2
        });
        Assert.Equal(HttpStatusCode.Forbidden, foreignReorder.StatusCode);

        var foreignDelete = await foreignClient.DeleteAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}?expectedGalleryVersion=2");
        Assert.Equal(HttpStatusCode.Forbidden, foreignDelete.StatusCode);

        var foreignProtectedRead = await foreignClient.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, foreignProtectedRead.StatusCode);
    }

    [Fact]
    public async Task UnauthorizedEditor_MissingCatalogManagePermission_IsDenied()
    {
        var (tenantId, ownerClient) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(ownerClient, tenantId, "Scarves", $"scarves-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(ownerClient, tenantId, categoryId, "Scarf", $"scarf-{Guid.NewGuid():N}"[..16]);

        // A real, active member with no role grant at all — same B035 shape
        // ShopCatalogAdminIntegrationTests already proves for category/product.
        var memberAccount = await CreateMemberAccountAsync(tenantId, $"member-{Guid.NewGuid():N}@tenantforge.local");
        using var memberClient = CreateClient();
        SetMemberToken(memberClient, memberAccount, $"member-{memberAccount.ToLong():x}@tenantforge.local");

        var response = await UploadAsync(memberClient, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DraftImage_ThroughThePublicByteRoute_IsDenied()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Draft Category", $"draft-{Guid.NewGuid():N}"[..14]);
        // Created inactive: current publication rules have no draft state on
        // ShopProduct beyond IsActive, so an inactive product is the "draft"
        // this scenario means.
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Draft Product", $"draftp-{Guid.NewGuid():N}"[..16], isActive: false);

        var upload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var gallery = await upload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        var imageId = gallery!.Images[0].Id;

        using var anonymous = CreateClient();
        var publicResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/media/{imageId}");
        Assert.Equal(HttpStatusCode.NotFound, publicResponse.StatusCode);
    }

    [Fact]
    public async Task ActiveImage_ThroughThePublicByteRoute_IsServed()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Live Category", $"live-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Live Product", $"livep-{Guid.NewGuid():N}"[..16]);

        var upload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);
        var gallery = await upload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        var imageId = gallery!.Images[0].Id;

        using var anonymous = CreateClient();
        var publicResponse = await anonymous.GetAsync($"/api/shop/{tenantId}/media/{imageId}");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Equal("nosniff", publicResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("immutable", publicResponse.Headers.CacheControl!.ToString());
        Assert.True((await publicResponse.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task DeletingTheLastRemainingImage_IsAllowed()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Minimal Category", $"minimal-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Minimal Product", $"minimalp-{Guid.NewGuid():N}"[..16]);

        var upload = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);
        var gallery = await upload.Content.ReadFromJsonAsync<GalleryDto>(Json);
        var imageId = gallery!.Images[0].Id;

        var delete = await client.DeleteAsync($"/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}?expectedGalleryVersion=2");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var product = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        using var productBody = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        Assert.Equal(0, productBody.RootElement.GetProperty("images").GetArrayLength());
    }

    [Fact]
    public async Task DbCommitFailureAfterStaging_RemovesTheStagedFile()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Fragile Category", $"fragile-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Fragile Product", $"fragilep-{Guid.NewGuid():N}"[..16]);
        var productTsid = TsidId.TryParseNullable(productId)!.Value;

        // Pre-insert a row directly (raw SQL — ShopProductImage is internal
        // to the Shop module) that collides on the real
        // ux_shop_product_images_product_display_order unique index at
        // DisplayOrder 0: the exact slot the upload handler computes for
        // this product's first image. Tagging it with a different, made-up
        // TenantId keeps it invisible to the handler's own tenant-scoped
        // imageCount query (so it neither trips the eight-image cap nor
        // changes the computed DisplayOrder) while still sharing the real
        // ProductId the physical index is keyed on. This forces a genuine
        // PostgreSQL unique-constraint violation inside SaveChangesAsync —
        // which only happens AFTER StageAsync has already written the temp
        // file — the exact "commit fails after staging" case this scenario
        // requires, without adding any test-only seam to production code.
        var foreignTenantId = TsidId.NewId();
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO shop_product_images
                    (id, tenant_id, product_id, storage_key, content_type, byte_length, width, height, alt_text, display_order, created_at_utc)
                VALUES (@id, @tenantId, @productId, @storageKey, 'image/webp', 100, 10, 10, '', 0, now());
                """;
            command.Parameters.AddWithValue("id", TsidId.NewId().ToLong());
            command.Parameters.AddWithValue("tenantId", foreignTenantId.ToLong());
            command.Parameters.AddWithValue("productId", productTsid.ToLong());
            command.Parameters.AddWithValue("storageKey", $"collision-{Guid.NewGuid():N}.webp");
            await command.ExecuteNonQueryAsync();
        }

        var stagingDirectory = Path.Combine(_factory.ShopMediaRoot, ".staging");
        var response = await UploadAsync(client, tenantId, productId, MakeJpeg(), expectedGalleryVersion: 1);

        // The forced unique-index violation surfaces as an unhandled
        // exception from SaveChangesAsync — this endpoint's only catch
        // clause cleans up the staged file and rethrows — so the request
        // never returns success.
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);

        // The staged temp file created for this failed attempt did not
        // survive it: LocalShopMediaStorage.DeleteIfExistsAsync ran from the
        // handler's catch block before the exception was rethrown.
        var remaining = Directory.Exists(stagingDirectory) ? Directory.GetFiles(stagingDirectory) : Array.Empty<string>();
        Assert.Empty(remaining);

        // Nothing from the failed attempt was persisted: the transaction
        // rolled back before the catch block ran, so the real product's
        // gallery is still empty and its version is unchanged.
        var product = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        using var productBody = JsonDocument.Parse(await product.Content.ReadAsStringAsync());
        Assert.Equal(0, productBody.RootElement.GetProperty("images").GetArrayLength());
        Assert.Equal(1, productBody.RootElement.GetProperty("galleryVersion").GetInt32());
    }

    [Fact]
    public async Task PreB036ProductResponse_StillDeserializes_WithEmptyImagesArray()
    {
        var (tenantId, client) = await NewTenantWithOwnerAsync();
        var categoryId = await CreateCategoryAsync(client, tenantId, "Legacy Category", $"legacy-{Guid.NewGuid():N}"[..14]);
        var productId = await CreateProductAsync(client, tenantId, categoryId, "Legacy Product", $"legacyp-{Guid.NewGuid():N}"[..16]);

        // No image uploaded at all — the exact "existing product" shape a
        // pre-B036 client already round-trips.
        var response = await client.GetAsync($"/api/tenants/{tenantId}/shop/products/{productId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        // Every pre-existing member is still present and unrenamed.
        Assert.True(TsidId.TryParse(root.GetProperty("id").GetString(), out _));
        Assert.Equal(tenantId, root.GetProperty("tenantId").GetString());
        Assert.Equal(categoryId, root.GetProperty("categoryId").GetString());
        Assert.Equal("Legacy Product", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, root.GetProperty("variants").GetArrayLength());

        // The new members are present, additive and empty/default for a
        // product that never touched B036's endpoints.
        Assert.Equal(0, root.GetProperty("images").GetArrayLength());
        Assert.Equal(1, root.GetProperty("galleryVersion").GetInt32());

        // The storefront summary/detail responses are equally backward
        // compatible: Images: [] there too.
        var storefrontDetail = await client.GetAsync($"/api/shop/{tenantId}/products/{root.GetProperty("slug").GetString()}");
        Assert.Equal(HttpStatusCode.OK, storefrontDetail.StatusCode);
        using var storefrontBody = JsonDocument.Parse(await storefrontDetail.Content.ReadAsStringAsync());
        Assert.Equal(0, storefrontBody.RootElement.GetProperty("images").GetArrayLength());
    }
}
