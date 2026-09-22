using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Categories;
using TenantForge.Modules.Shop.Features.Media;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Storefront;

internal static class StorefrontCatalogFeature
{
    public static IEndpointRouteBuilder MapStorefrontCatalogFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/shop/{tenantId}/categories", async (string tenantId, ShopDbContext db, CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            // B038: load every effectively-active category, then nest each
            // direct child under its root. A child whose parent is inactive is
            // dropped by the predicate itself; a root with no children keeps
            // Children: [].
            var categories = await db.Categories.AsNoTracking()
                .Where(category => category.TenantId == tenantTsid)
                .Where(CategoryVisibility.For(db.Categories))
                .OrderBy(category => category.DisplayOrder)
                .ThenBy(category => category.Id)
                .ToListAsync(ct);

            // Group children under their roots, both in displayOrder then id.
            var childGroups = categories
                .Where(category => category.ParentCategoryId is not null)
                .GroupBy(category => category.ParentCategoryId!.Value.ToLong())
                .ToDictionary(group => group.Key);

            var roots = categories
                .Where(category => category.ParentCategoryId is null)
                .Select(category =>
                {
                    var children = childGroups.TryGetValue(category.Id.ToLong(), out var childGroup)
                        ? childGroup
                            .OrderBy(child => child.DisplayOrder)
                            .ThenBy(child => child.Id)
                            .Select(child => new StorefrontCategoryResponse(
                                TsidId.Format(child.Id), child.Name, child.Slug, child.DisplayOrder, []))
                            .ToList()
                        : [];
                    return new StorefrontCategoryResponse(
                        TsidId.Format(category.Id), category.Name, category.Slug, category.DisplayOrder, children);
                })
                .ToList();

            return Results.Ok(new StorefrontCategoryListResponse(roots));
        });

        endpoints.MapGet("/api/shop/{tenantId}/categories/{categorySlug}/products", async (
            string tenantId,
            string categorySlug,
            HttpRequest request,
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var normalizedSlug = categorySlug.Trim().ToLowerInvariant();
            // B038: the category must itself be effectively active (a child
            // whose root is inactive is a 404, like a nonexistent slug).
            var category = await db.Categories.AsNoTracking()
                .Where(category => category.TenantId == tenantTsid && category.Slug == normalizedSlug)
                .Where(CategoryVisibility.For(db.Categories))
                .SingleOrDefaultAsync(ct);
            if (category is null) return Results.NotFound();

            // B038: a root slug lists the root's own products plus those of
            // its direct children; a child slug lists only that child. The
            // booleans/ids are captured as plain values so the predicate
            // stays an expression tree EF Core can translate.
            var isRootCategory = category.ParentCategoryId is null;
            var categoryIdValue = category.Id;
            var query = db.Products.AsNoTracking()
                .Where(product => product.TenantId == tenantTsid && product.IsActive)
                .Where(product =>
                    isRootCategory
                        ? product.CategoryId == categoryIdValue
                            || db.Categories.Any(childCategory => childCategory.Id == product.CategoryId && childCategory.ParentCategoryId == categoryIdValue)
                        : product.CategoryId == categoryIdValue)
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id);

            var (products, pagination) = await PaginationSupport.PageAsync(query, page, ct);
            var summaries = new List<StorefrontProductSummaryResponse>();
            foreach (var product in products)
            {
                var productRouteId = TsidId.Format(product.Id);
                var tenantRouteId = TsidId.Format(product.TenantId);
                var gallery = await ProductMediaFeature.LoadGalleryAsync(db, tenantTsid, product.Id, tenantRouteId, productRouteId, publicUrls: true, ct);
                var hasInStockVariant = await db.ProductVariants.AnyAsync(variant => variant.ProductId == product.Id && variant.StockQuantity > 0, ct);
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

            // B038: resolving the slug requires the category to be
            // effectively active, and a root slug includes its direct
            // children's products (a child slug is handled by the
            // childParentId branch below).
            Tsid? categoryId = null;
            Tsid? categorySlugChildParentId = null;
            if (!string.IsNullOrWhiteSpace(categorySlug))
            {
                var normalizedSlug = categorySlug.Trim().ToLowerInvariant();
                var category = await db.Categories.AsNoTracking()
                    .Where(category => category.TenantId == tenantTsid && category.Slug == normalizedSlug)
                    .Where(CategoryVisibility.For(db.Categories))
                    .SingleOrDefaultAsync(ct);
                if (category is null) return Results.NotFound();
                categoryId = category.Id;
                categorySlugChildParentId = category.ParentCategoryId;
            }

            // B038: a product is public only while its category is effectively
            // active — the shared predicate, so this route and the public
            // category list cannot drift apart.
            var publicCategories = db.Categories.AsNoTracking()
                .Where(category => category.TenantId == tenantTsid)
                .Where(CategoryVisibility.For(db.Categories));

            var query = db.Products.AsNoTracking()
                .Where(product => product.TenantId == tenantTsid && product.IsActive)
                .Where(product => publicCategories.Any(category => category.Id == product.CategoryId));

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
                // A root slug: the root's own products plus the products of
                // its direct children. A child slug: that child's products only.
                // Captured as plain values so the predicate translates.
                var isRootSlug = categorySlugChildParentId is null;
                var slugCategoryId = categoryId.Value;
                query = query.Where(product =>
                    isRootSlug
                        ? product.CategoryId == slugCategoryId
                            || db.Categories.Any(category => category.Id == product.CategoryId && category.ParentCategoryId == slugCategoryId)
                        : product.CategoryId == slugCategoryId);
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
            ShopDbContext db,
            CancellationToken ct) =>
        {
            if (!TsidId.TryParse(tenantId, out var tenantTsid)) return Results.NotFound();

            var normalizedSlug = productSlug.Trim().ToLowerInvariant();
            // B038: the product's category must be effectively active too —
            // deactivating a root hides its products' detail pages as well.
            var publicCategories = db.Categories.AsNoTracking()
                .Where(category => category.TenantId == tenantTsid)
                .Where(CategoryVisibility.For(db.Categories));

            var product = await (
                from p in db.Products.AsNoTracking()
                join c in publicCategories on p.CategoryId equals c.Id
                where p.TenantId == tenantTsid && p.Slug == normalizedSlug && p.IsActive
                select p).SingleOrDefaultAsync(ct);
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
                .ToListAsync(ct);

            var columns = await db.SizeGuideColumns.AsNoTracking()
                .Where(column => column.ProductId == product.Id)
                .OrderBy(column => column.DisplayOrder)
                .ToListAsync(ct);

            var rows = await db.SizeGuideRows.AsNoTracking()
                .Where(row => row.ProductId == product.Id)
                .OrderBy(row => row.DisplayOrder)
                .ToListAsync(ct);

            var rowIds = rows.Select(row => row.Id).ToList();
            var cells = await db.SizeGuideCells.AsNoTracking()
                .Where(cell => rowIds.Contains(cell.RowId))
                .ToListAsync(ct);

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
            var gallery = await ProductMediaFeature.LoadGalleryAsync(db, tenantTsid, product.Id, tenantRouteId, productRouteId, publicUrls: true, ct);

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
