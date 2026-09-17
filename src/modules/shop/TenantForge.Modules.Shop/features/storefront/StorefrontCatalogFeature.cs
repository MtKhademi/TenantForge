using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
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
            var summaries = products.Select(product => new StorefrontProductSummaryResponse(
                TsidId.Format(product.Id),
                product.Name,
                product.Slug,
                product.BasePrice,
                product.CompareAtPrice)).ToList();

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

            var response = new StorefrontProductDetailResponse(
                TsidId.Format(product.Id),
                TsidId.Format(product.CategoryId),
                product.Name,
                product.Slug,
                product.Description,
                product.BasePrice,
                product.CompareAtPrice,
                variants,
                columns.Select(column => new StorefrontSizeGuideColumnResponse(
                    TsidId.Format(column.Id), column.Name, column.DisplayOrder)).ToList(),
                rowResponses);

            return Results.Ok(response);
        });

        return endpoints;
    }
}
