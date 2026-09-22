using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Media;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Storefront;

internal static class StorefrontCatalogFeature
{
    public static IEndpointRouteBuilder MapStorefrontCatalogFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/shop/{tenantId}/categories", async (string tenantId, ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var categories = await db.Categories.AsNoTracking()
                .Where(category => category.TenantId == tenantTsid && category.IsActive)
                .OrderBy(category => category.DisplayOrder)
                .ThenBy(category => category.Id)
                .Select(category => new StorefrontCategoryResponse(
                    TsidId.Format(category.Id), category.Name, category.Slug, category.DisplayOrder))
                .ToListAsync();

            return Results.Ok(new StorefrontCategoryListResponse(categories));
        });

        endpoints.MapGet("/api/shop/{tenantId}/categories/{categorySlug}/products", async (
            string tenantId,
            string categorySlug,
            HttpRequest request,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var normalizedSlug = categorySlug.Trim().ToLowerInvariant();
            var category = await db.Categories.AsNoTracking()
                .SingleOrDefaultAsync(category =>
                    category.TenantId == tenantTsid && category.Slug == normalizedSlug && category.IsActive);
            if (category is null) return Results.NotFound();

            var query = db.Products.AsNoTracking()
                .Where(product => product.TenantId == tenantTsid && product.CategoryId == category.Id && product.IsActive)
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id);

            var (products, pagination) = await PaginationSupport.PageAsync(query, page);
            var summaries = new List<StorefrontProductSummaryResponse>();
            foreach (var product in products)
            {
                var productRouteId = TsidId.Format(product.Id);
                var tenantRouteId = TsidId.Format(product.TenantId);
                var gallery = await ProductMediaFeature.LoadGalleryAsync(db, tenantTsid, product.Id, tenantRouteId, productRouteId, publicUrls: true, CancellationToken.None);
                var hasInStockVariant = await db.ProductVariants.AnyAsync(variant => variant.ProductId == product.Id && variant.StockQuantity > 0);
                summaries.Add(new StorefrontProductSummaryResponse(
                    productRouteId,
                    product.Name,
                    product.Slug,
                    product.BasePrice,
                    product.CompareAtPrice,
                    product.CompareAtPrice is { } compareAtPrice && compareAtPrice > product.BasePrice,
                    !hasInStockVariant,
                    gallery.Images.FirstOrDefault()?.ContentUrl,
                    gallery.Images));
            }

            return Results.Ok(new StorefrontProductListResponse(summaries, pagination));
        });

        endpoints.MapGet("/api/shop/{tenantId}/products", async (
            string tenantId,
            string? q,
            string? categorySlug,
            string? sort,
            bool? saleOnly,
            HttpRequest request,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            if (!TryParseStorefrontSort(sort, out var storefrontSort))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sort"] = ["sort must be one of: newest, price-asc, price-desc, name."]
                });
            }

            Tsid? categoryId = null;
            if (!string.IsNullOrWhiteSpace(categorySlug))
            {
                var normalizedSlug = categorySlug.Trim().ToLowerInvariant();
                var category = await db.Categories.AsNoTracking()
                    .SingleOrDefaultAsync(category =>
                        category.TenantId == tenantTsid && category.Slug == normalizedSlug && category.IsActive, ct);
                if (category is null) return Results.NotFound();
                categoryId = category.Id;
            }

            var query = db.Products.AsNoTracking()
                .Where(product => product.TenantId == tenantTsid && product.IsActive)
                .Where(product => db.Categories.Any(category => category.Id == product.CategoryId && category.TenantId == tenantTsid && category.IsActive));

            var search = q?.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                // Truncated before reaching the database: the pattern is a
                // captured constant, so Npgsql sends it as a bound parameter —
                // never concatenated into SQL text.
                var truncated = search.Length > MaxSearchLength ? search[..MaxSearchLength] : search;
                query = query.Where(product => EF.Functions.ILike(product.Name, $"%{truncated}%"));
            }

            if (categoryId is not null)
            {
                query = query.Where(product => product.CategoryId == categoryId.Value);
            }

            // The card price is the lowest in-stock variant's effective price,
            // computed in SQL as a correlated scalar subquery (the inline
            // Min() below). EF Core translates this into a SQL MIN(...)
            // subquery; a method call returning an IQueryable is not
            // recognized, so the subquery is inlined. The same value drives
            // the saleOnly filter, the price sorts and the response.
            var displayed = query
                .Select(product => new
                {
                    product.Id,
                    product.Name,
                    product.Slug,
                    product.BasePrice,
                    product.CompareAtPrice,
                    CardPrice = db.ProductVariants
                        .Where(variant => variant.ProductId == product.Id && variant.StockQuantity > 0)
                        .Select(variant => (decimal?)(variant.PriceOverride ?? product.BasePrice))
                        .Min()
                })
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.Slug,
                    item.CompareAtPrice,
                    IsSoldOut = item.CardPrice == null,
                    DisplayPrice = item.CardPrice ?? item.BasePrice,
                    IsOnSale = item.CompareAtPrice != null && item.CompareAtPrice > (item.CardPrice ?? item.BasePrice)
                });

            if (saleOnly is true)
            {
                displayed = displayed.Where(item => item.IsOnSale);
            }

            // Sold-out products always list last, whatever the chosen order.
            // The chosen sort is a secondary key (ThenBy...) so the sold-out
            // rule stays the primary ordering.
            var ordered = displayed.OrderBy(item => item.IsSoldOut);
            ordered = storefrontSort switch
            {
                StorefrontSortOption.PriceAsc => ordered.ThenBy(item => item.DisplayPrice),
                StorefrontSortOption.PriceDesc => ordered.ThenByDescending(item => item.DisplayPrice),
                StorefrontSortOption.Name => ordered.ThenBy(item => item.Name),
                _ => ordered.ThenByDescending(item => item.Id)
            };
            ordered = ordered.ThenBy(item => item.Id);

            var (items, pagination) = await PaginationSupport.PageAsync(ordered, page, ct);
            var summaries = new List<StorefrontProductSummaryResponse>();
            var tenantRouteId = TsidId.Format(tenantTsid);
            foreach (var item in items)
            {
                var productRouteId = TsidId.Format(item.Id);
                var gallery = await ProductMediaFeature.LoadGalleryAsync(db, tenantTsid, item.Id, tenantRouteId, productRouteId, publicUrls: true, ct);
                summaries.Add(new StorefrontProductSummaryResponse(
                    productRouteId,
                    item.Name,
                    item.Slug,
                    item.DisplayPrice,
                    item.CompareAtPrice,
                    item.IsOnSale,
                    item.IsSoldOut,
                    gallery.Images.FirstOrDefault()?.ContentUrl,
                    gallery.Images));
            }

            return Results.Ok(new StorefrontProductListResponse(summaries, pagination));
        });

        endpoints.MapGet("/api/shop/{tenantId}/products/{productSlug}", async (
            string tenantId,
            string productSlug,
            ShopDbContext db) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var normalizedSlug = productSlug.Trim().ToLowerInvariant();
            var product = await db.Products.AsNoTracking()
                .SingleOrDefaultAsync(product =>
                    product.TenantId == tenantTsid && product.Slug == normalizedSlug && product.IsActive);
            if (product is null) return Results.NotFound();

            var variants = await db.ProductVariants.AsNoTracking()
                .Where(variant => variant.ProductId == product.Id)
                .OrderBy(variant => variant.Color).ThenBy(variant => variant.Size)
                .Select(variant => new StorefrontVariantResponse(
                    TsidId.Format(variant.Id),
                    variant.Color,
                    variant.Size,
                    variant.StockQuantity,
                    variant.PriceOverride ?? product.BasePrice))
                .ToListAsync();

            var columns = await db.SizeGuideColumns.AsNoTracking()
                .Where(column => column.ProductId == product.Id)
                .OrderBy(column => column.DisplayOrder)
                .ToListAsync();

            var rows = await db.SizeGuideRows.AsNoTracking()
                .Where(row => row.ProductId == product.Id)
                .OrderBy(row => row.DisplayOrder)
                .ToListAsync();

            var rowIds = rows.Select(row => row.Id).ToList();
            var cells = await db.SizeGuideCells.AsNoTracking()
                .Where(cell => rowIds.Contains(cell.RowId))
                .ToListAsync();

            var rowResponses = rows.Select(row => new StorefrontSizeGuideRowResponse(
                row.SizeLabel,
                row.DisplayOrder,
                columns.Select(column =>
                {
                    var cell = cells.SingleOrDefault(c => c.RowId == row.Id && c.ColumnId == column.Id);
                    return new StorefrontSizeGuideCellResponse(TsidId.Format(column.Id), cell?.Value ?? string.Empty);
                }).ToList())).ToList();

            var productRouteId = TsidId.Format(product.Id);
            var tenantRouteId = TsidId.Format(product.TenantId);
            var gallery = await ProductMediaFeature.LoadGalleryAsync(db, tenantTsid, product.Id, tenantRouteId, productRouteId, publicUrls: true, CancellationToken.None);

            var response = new StorefrontProductDetailResponse(
                productRouteId,
                TsidId.Format(product.CategoryId),
                product.Name,
                product.Slug,
                product.Description,
                product.BasePrice,
                product.CompareAtPrice,
                gallery.Images,
                variants,
                columns.Select(column => new StorefrontSizeGuideColumnResponse(
                    TsidId.Format(column.Id), column.Name, column.DisplayOrder)).ToList(),
                rowResponses);

            return Results.Ok(response);
        });

        return endpoints;
    }

    private const int MaxSearchLength = 100;

    private enum StorefrontSortOption
    {
        Newest,
        PriceAsc,
        PriceDesc,
        Name
    }

    private static bool TryParseStorefrontSort(string? sort, out StorefrontSortOption option)
    {
        option = StorefrontSortOption.Newest;
        return sort switch
        {
            "newest" or null => true,
            "price-asc" => Set(StorefrontSortOption.PriceAsc, ref option),
            "price-desc" => Set(StorefrontSortOption.PriceDesc, ref option),
            "name" => Set(StorefrontSortOption.Name, ref option),
            _ => false
        };

        static bool Set(StorefrontSortOption value, ref StorefrontSortOption target)
        {
            target = value;
            return true;
        }
    }
}
