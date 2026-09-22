using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Media;

internal static class ProductMediaFeature
{
    private const int MaxImagesPerProduct = 8;

    public static IEndpointRouteBuilder MapProductMediaFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/products/{productId}/images", async (
            string tenantId,
            string productId,
            [FromForm] UploadProductImageRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db,
            ShopImageValidator validator,
            IShopMediaStorage storage,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;
            if (!TsidId.TryParse(productId, out var productTsid)) return Results.NotFound();
            if (request.File.Length <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Image file is required."] });

            SanitizedShopImage sanitized;
            try
            {
                await using var input = request.File.OpenReadStream();
                sanitized = await validator.ValidateAndReencodeAsync(input, ct);
            }
            catch (ShopImageValidationException ex)
            {
                return ex.Failure == ShopImageValidationFailure.Oversized
                    ? Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Image is too large.", detail: ex.Message)
                    : Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Unsupported image.", detail: ex.Message);
            }

            StagedShopMedia? staged = null;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var product = await db.Products.SingleOrDefaultAsync(product => product.TenantId == access.TenantId && product.Id == productTsid, ct);
                if (product is null) return Results.NotFound();
                if (product.GalleryVersion != request.ExpectedGalleryVersion) return GalleryConflictProblem();

                var imageCount = await db.ProductImages.CountAsync(image => image.TenantId == access.TenantId && image.ProductId == productTsid, ct);
                if (imageCount >= MaxImagesPerProduct)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["A product gallery can contain at most eight images."] });
                }

                staged = await storage.StageAsync(sanitized.Content, ct);
                var image = ShopProductImage.Create(
                    access.TenantId,
                    productTsid,
                    staged.StorageKey,
                    sanitized.ByteLength,
                    sanitized.Width,
                    sanitized.Height,
                    request.AltText,
                    imageCount,
                    clock.GetUtcNow());
                db.ProductImages.Add(image);
                product.IncrementGalleryVersion();
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                await storage.CommitAsync(staged, ct);

                var gallery = await LoadGalleryAsync(db, access.TenantId, productTsid, tenantId, productId, publicUrls: false, ct);
                return Results.Created($"/api/tenants/{tenantId}/shop/products/{productId}/images/{TsidId.Format(image.Id)}", gallery);
            }
            catch
            {
                if (staged is not null) await storage.DeleteIfExistsAsync(staged.StorageKey, CancellationToken.None);
                throw;
            }
        }).RequireAuthorization().DisableAntiforgery();

        endpoints.MapPut("/api/tenants/{tenantId}/shop/products/{productId}/images/order", async (
            string tenantId,
            string productId,
            ReorderProductImagesRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;
            if (!TsidId.TryParse(productId, out var productTsid)) return Results.NotFound();
            if (request.ImageIds is null || request.ImageIds.Count == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["imageIds"] = ["ImageIds is required."] });
            }

            var requestedIds = new List<Tsid>();
            foreach (var id in request.ImageIds)
            {
                if (!TsidId.TryParse(id, out var imageTsid)) return Results.NotFound();
                requestedIds.Add(imageTsid);
            }

            if (requestedIds.Distinct().Count() != requestedIds.Count)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["imageIds"] = ["ImageIds cannot contain duplicates."] });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var product = await db.Products.SingleOrDefaultAsync(product => product.TenantId == access.TenantId && product.Id == productTsid, ct);
            if (product is null) return Results.NotFound();
            if (product.GalleryVersion != request.ExpectedGalleryVersion) return GalleryConflictProblem();

            var images = await db.ProductImages
                .Where(image => image.TenantId == access.TenantId && image.ProductId == productTsid)
                .ToListAsync(ct);
            if (images.Count != requestedIds.Count || images.Select(image => image.Id).Except(requestedIds).Any()) return Results.NotFound();

            // The unique (ProductId, DisplayOrder) index is checked immediately
            // by PostgreSQL, not deferred to commit. A reorder that is not a
            // pure compaction (for example, swapping two positions) would
            // otherwise collide mid-update, because EF issues the row UPDATEs
            // in an order this code does not control. Moving every row to a
            // negative, guaranteed-unique placeholder first (no existing row
            // ever has a negative DisplayOrder) and saving, then assigning the
            // real 0-based positions and saving again, keeps every
            // intermediate state collision-free while staying inside the same
            // transaction.
            for (var index = 0; index < requestedIds.Count; index++)
            {
                images.Single(image => image.Id == requestedIds[index]).MoveToTemporarySlot(-(index + 1));
            }
            await db.SaveChangesAsync(ct);

            for (var index = 0; index < requestedIds.Count; index++)
            {
                images.Single(image => image.Id == requestedIds[index]).MoveTo(index);
            }

            product.IncrementGalleryVersion();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(await LoadGalleryAsync(db, access.TenantId, productTsid, tenantId, productId, publicUrls: false, ct));
        }).RequireAuthorization();

        endpoints.MapDelete("/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}", async (
            string tenantId,
            string productId,
            string imageId,
            int expectedGalleryVersion,
            ClaimsPrincipal principal,
            ShopDbContext db,
            IShopMediaStorage storage,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;
            if (!TsidId.TryParse(productId, out var productTsid) || !TsidId.TryParse(imageId, out var imageTsid)) return Results.NotFound();

            string? removedStorageKey = null;
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                var product = await db.Products.SingleOrDefaultAsync(product => product.TenantId == access.TenantId && product.Id == productTsid, ct);
                if (product is null) return Results.NotFound();
                if (product.GalleryVersion != expectedGalleryVersion) return GalleryConflictProblem();

                var image = await db.ProductImages.SingleOrDefaultAsync(image => image.TenantId == access.TenantId && image.ProductId == productTsid && image.Id == imageTsid, ct);
                if (image is null) return Results.NotFound();
                removedStorageKey = image.StorageKey;
                db.ProductImages.Remove(image);

                var remaining = await db.ProductImages
                    .Where(candidate => candidate.TenantId == access.TenantId && candidate.ProductId == productTsid && candidate.Id != imageTsid)
                    .OrderBy(candidate => candidate.DisplayOrder)
                    .ToListAsync(ct);
                for (var index = 0; index < remaining.Count; index++) remaining[index].MoveTo(index);

                product.IncrementGalleryVersion();
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }

            if (removedStorageKey is not null) await storage.DeleteIfExistsAsync(removedStorageKey, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content", async (
            string tenantId,
            string productId,
            string imageId,
            ClaimsPrincipal principal,
            HttpResponse response,
            ShopDbContext db,
            IShopMediaStorage storage,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;
            if (!TsidId.TryParse(productId, out var productTsid) || !TsidId.TryParse(imageId, out var imageTsid)) return Results.NotFound();

            var image = await db.ProductImages.AsNoTracking().SingleOrDefaultAsync(image => image.TenantId == access.TenantId && image.ProductId == productTsid && image.Id == imageTsid, ct);
            if (image is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(image.StorageKey, ct);
            if (stream is null) return Results.NotFound();
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "no-store";
            return Results.File(stream, image.ContentType);
        }).RequireAuthorization();

        endpoints.MapGet("/api/shop/{tenantId}/media/{imageId}", async (
            string tenantId,
            string imageId,
            HttpResponse response,
            ShopDbContext db,
            IShopMediaStorage storage,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid) || !TsidId.TryParse(imageId, out var imageTsid)) return Results.NotFound();

            var image = await (
                from productImage in db.ProductImages.AsNoTracking()
                join product in db.Products.AsNoTracking() on productImage.ProductId equals product.Id
                join category in db.Categories.AsNoTracking() on product.CategoryId equals category.Id
                where productImage.TenantId == tenantTsid
                    && productImage.Id == imageTsid
                    && product.TenantId == tenantTsid
                    && category.TenantId == tenantTsid
                    && product.IsActive
                    && category.IsActive
                select productImage).SingleOrDefaultAsync(ct);
            if (image is null) return Results.NotFound();

            var stream = await storage.OpenReadAsync(image.StorageKey, ct);
            if (stream is null) return Results.NotFound();
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(stream, image.ContentType);
        });

        return endpoints;
    }

    internal static async Task<ProductGalleryResponse> LoadGalleryAsync(
        ShopDbContext db,
        Tsid tenantId,
        Tsid productId,
        string tenantRouteId,
        string productRouteId,
        bool publicUrls,
        CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleAsync(product => product.TenantId == tenantId && product.Id == productId, ct);
        var images = await db.ProductImages.AsNoTracking()
            .Where(image => image.TenantId == tenantId && image.ProductId == productId)
            .OrderBy(image => image.DisplayOrder)
            .ThenBy(image => image.Id)
            .Select(image => new ProductImageResponse(
                TsidId.Format(image.Id),
                image.AltText,
                image.DisplayOrder,
                image.Width,
                image.Height,
                publicUrls
                    ? $"/api/shop/{tenantRouteId}/media/{TsidId.Format(image.Id)}"
                    : $"/api/tenants/{tenantRouteId}/shop/products/{productRouteId}/images/{TsidId.Format(image.Id)}/content"))
            .ToListAsync(ct);

        return new ProductGalleryResponse(images, product.GalleryVersion);
    }

    private static IResult GalleryConflictProblem() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Gallery version conflict.", detail: "The product gallery changed before this request was applied.");
}
