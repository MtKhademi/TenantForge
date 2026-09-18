using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Features.Authorization;
using TenantForge.Modules.Shop.Features.Pagination;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Products;

internal static class ProductsFeature
{
    public static IEndpointRouteBuilder MapProductsFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/products", async (
            string tenantId,
            CreateProductRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;

            var errors = await ValidateProductRequestAsync(db, access.TenantId, request.Name, request.Slug, request.CategoryId, request.Variants, request.SizeGuideColumns, request.SizeGuideRows, existingProductId: null);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var categoryTsid = TsidId.TryParseNullable(request.CategoryId)!.Value;
            var slug = request.Slug!.Trim().ToLowerInvariant();

            await using var transaction = await db.Database.BeginTransactionAsync();

            var product = ShopProduct.Create(
                access.TenantId, categoryTsid, request.Name!, slug, request.Description ?? string.Empty,
                request.BasePrice, request.CompareAtPrice);
            db.Products.Add(product);

            // Save the product before adding its variants and size-guide rows:
            // the B025 domain model has no ShopProduct -> child navigation
            // properties, so EF cannot infer the FK target until the product
            // row is persisted. Saving it first makes product.Id a real row
            // for PostgreSQL's shop_product_variants.product_id FK to point
            // at, and keeps everything inside the same transaction.
            await db.SaveChangesAsync();

            AddVariantsAndSizeGuide(db, product.Id, request.Variants!, request.SizeGuideColumns, request.SizeGuideRows);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = await LoadProductResponseAsync(db, access.TenantId, product.Id);
            return Results.Created($"/api/tenants/{tenantId}/shop/products/{TsidId.Format(product.Id)}", response);
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/products", async (
            string tenantId,
            HttpRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!PaginationSupport.TryBind(request, out var page, out var errors))
            {
                return Results.ValidationProblem(errors);
            }

            var query = db.Products.AsNoTracking()
                .Where(product => product.TenantId == access.TenantId)
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id);

            var (products, pagination) = await PaginationSupport.PageAsync(query, page);
            var summaries = new List<ProductSummaryResponse>();
            foreach (var product in products)
            {
                var variantCount = await db.ProductVariants.CountAsync(variant => variant.ProductId == product.Id);
                summaries.Add(new ProductSummaryResponse(
                    TsidId.Format(product.Id), product.Name, product.Slug,
                    TsidId.Format(product.CategoryId), product.BasePrice, product.IsActive, variantCount));
            }

            return Results.Ok(new ProductListResponse(summaries, pagination));
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/products/{productId}", async (
            string tenantId,
            string productId,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(productId, out var productTsid)) return Results.NotFound();

            var response = await LoadProductResponseAsync(db, access.TenantId, productTsid);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/shop/products/{productId}", async (
            string tenantId,
            string productId,
            UpdateProductRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(productId, out var productTsid)) return Results.NotFound();

            var errors = await ValidateProductRequestAsync(db, access.TenantId, request.Name, request.Slug, request.CategoryId, request.Variants, request.SizeGuideColumns, request.SizeGuideRows, existingProductId: productTsid);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await using var transaction = await db.Database.BeginTransactionAsync();

            var product = await db.Products.SingleOrDefaultAsync(product =>
                product.TenantId == access.TenantId && product.Id == productTsid);
            if (product is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound();
            }

            var categoryTsid = TsidId.TryParseNullable(request.CategoryId)!.Value;
            var slug = request.Slug!.Trim().ToLowerInvariant();
            product.Update(categoryTsid, request.Name!, slug, request.Description ?? string.Empty, request.BasePrice, request.CompareAtPrice, request.IsActive);

            // Replace-wholesale: remove every existing variant/size-guide row
            // for this product, then re-add from the submitted payload. The
            // admin form always submits the complete current state (B026's
            // Spec Scope), so a diff is unnecessary complexity.
            var oldVariants = db.ProductVariants.Where(variant => variant.ProductId == productTsid);
            db.ProductVariants.RemoveRange(oldVariants);
            var oldColumns = db.SizeGuideColumns.Where(column => column.ProductId == productTsid);
            var oldRows = db.SizeGuideRows.Where(row => row.ProductId == productTsid);
            var oldRowIds = await oldRows.Select(row => row.Id).ToListAsync();
            db.SizeGuideCells.RemoveRange(db.SizeGuideCells.Where(cell => oldRowIds.Contains(cell.RowId)));
            db.SizeGuideRows.RemoveRange(oldRows);
            db.SizeGuideColumns.RemoveRange(oldColumns);
            await db.SaveChangesAsync();

            AddVariantsAndSizeGuide(db, productTsid, request.Variants!, request.SizeGuideColumns, request.SizeGuideRows);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var response = await LoadProductResponseAsync(db, access.TenantId, productTsid);
            return Results.Ok(response);
        }).RequireAuthorization();

        return endpoints;
    }

    private static void AddVariantsAndSizeGuide(
        ShopDbContext db,
        Tsid productId,
        IReadOnlyList<ProductVariantInput> variants,
        IReadOnlyList<string>? sizeGuideColumns,
        IReadOnlyList<SizeGuideRowInput>? sizeGuideRows)
    {
        foreach (var variant in variants)
        {
            db.ProductVariants.Add(ShopProductVariant.Create(
                productId, variant.Color ?? string.Empty, variant.Size ?? string.Empty,
                variant.Sku ?? string.Empty, variant.StockQuantity, variant.PriceOverride));
        }

        if (sizeGuideColumns is null || sizeGuideColumns.Count == 0) return;

        var columns = new List<ShopSizeGuideColumn>();
        for (var i = 0; i < sizeGuideColumns.Count; i++)
        {
            var column = ShopSizeGuideColumn.Create(productId, sizeGuideColumns[i], i);
            columns.Add(column);
            db.SizeGuideColumns.Add(column);
        }

        if (sizeGuideRows is null) return;

        for (var rowIndex = 0; rowIndex < sizeGuideRows.Count; rowIndex++)
        {
            var rowInput = sizeGuideRows[rowIndex];
            var row = ShopSizeGuideRow.Create(productId, rowInput.SizeLabel ?? string.Empty, rowIndex);
            db.SizeGuideRows.Add(row);

            var values = rowInput.Values ?? [];
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var value = columnIndex < values.Count ? values[columnIndex] : string.Empty;
                db.SizeGuideCells.Add(ShopSizeGuideCell.Create(row.Id, columns[columnIndex].Id, value));
            }
        }
    }

    private static async Task<Dictionary<string, string[]>> ValidateProductRequestAsync(
        ShopDbContext db,
        Tsid tenantId,
        string? name,
        string? slug,
        string? categoryId,
        IReadOnlyList<ProductVariantInput>? variants,
        IReadOnlyList<string>? sizeGuideColumns,
        IReadOnlyList<SizeGuideRowInput>? sizeGuideRows,
        Tsid? existingProductId)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Product name is required."];
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            errors["slug"] = ["Product slug is required."];
        }
        else
        {
            var normalizedSlug = slug.Trim().ToLowerInvariant();
            var duplicate = await db.Products.AnyAsync(product =>
                product.TenantId == tenantId
                && product.Slug == normalizedSlug
                && (existingProductId == null || product.Id != existingProductId.Value));
            if (duplicate)
            {
                errors["slug"] = ["A product with this slug already exists in this tenant."];
            }
        }

        var categoryTsid = TsidId.TryParseNullable(categoryId);
        if (categoryTsid is null)
        {
            errors["categoryId"] = ["A valid category is required."];
        }
        else
        {
            var categoryExists = await db.Categories.AnyAsync(category =>
                category.TenantId == tenantId && category.Id == categoryTsid.Value);
            if (!categoryExists)
            {
                errors["categoryId"] = ["The selected category does not belong to this tenant."];
            }
        }

        if (variants is null || variants.Count == 0)
        {
            errors["variants"] = ["At least one color/size variant is required."];
        }
        else
        {
            var pairs = variants.Select(v => (v.Color?.Trim(), v.Size?.Trim())).ToList();
            if (pairs.Distinct().Count() != pairs.Count)
            {
                errors["variants"] = ["Each color/size combination must be unique."];
            }
        }

        if (sizeGuideColumns is { Count: > 0 } && sizeGuideRows is not null)
        {
            var mismatch = sizeGuideRows.Any(row => (row.Values?.Count ?? 0) != sizeGuideColumns.Count);
            if (mismatch)
            {
                errors["sizeGuideRows"] = ["Every size-guide row must have exactly one value per column."];
            }
        }

        return errors;
    }

    private static async Task<ProductResponse?> LoadProductResponseAsync(ShopDbContext db, Tsid tenantId, Tsid productId)
    {
        var product = await db.Products.AsNoTracking()
            .SingleOrDefaultAsync(product => product.TenantId == tenantId && product.Id == productId);
        if (product is null) return null;

        var variants = await db.ProductVariants.AsNoTracking()
            .Where(variant => variant.ProductId == productId)
            .OrderBy(variant => variant.Color).ThenBy(variant => variant.Size)
            .Select(variant => new ProductVariantResponse(
                TsidId.Format(variant.Id), variant.Color, variant.Size, variant.Sku,
                variant.StockQuantity, variant.PriceOverride))
            .ToListAsync();

        var columns = await db.SizeGuideColumns.AsNoTracking()
            .Where(column => column.ProductId == productId)
            .OrderBy(column => column.DisplayOrder)
            .ToListAsync();

        var rows = await db.SizeGuideRows.AsNoTracking()
            .Where(row => row.ProductId == productId)
            .OrderBy(row => row.DisplayOrder)
            .ToListAsync();

        var rowIds = rows.Select(row => row.Id).ToList();
        var cells = await db.SizeGuideCells.AsNoTracking()
            .Where(cell => rowIds.Contains(cell.RowId))
            .ToListAsync();

        var rowResponses = rows.Select(row => new SizeGuideRowResponse(
            TsidId.Format(row.Id),
            row.SizeLabel,
            row.DisplayOrder,
            columns.Select(column =>
            {
                var cell = cells.SingleOrDefault(c => c.RowId == row.Id && c.ColumnId == column.Id);
                return new SizeGuideCellResponse(TsidId.Format(column.Id), cell?.Value ?? string.Empty);
            }).ToList())).ToList();

        return new ProductResponse(
            TsidId.Format(product.Id),
            TsidId.Format(product.TenantId),
            TsidId.Format(product.CategoryId),
            product.Name,
            product.Slug,
            product.Description,
            product.BasePrice,
            product.CompareAtPrice,
            product.IsActive,
            variants,
            columns.Select(column => new SizeGuideColumnResponse(TsidId.Format(column.Id), column.Name, column.DisplayOrder)).ToList(),
            rowResponses);
    }
}
