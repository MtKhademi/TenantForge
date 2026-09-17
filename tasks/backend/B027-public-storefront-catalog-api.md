---
id: B027
slice: S26
title: Public storefront catalog read API
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Add the first anonymous Shop endpoints: list active categories, list
active products within a category (paginated), and fetch one active
product's full detail (variants with live stock, size-guide table) by
slug — all unauthenticated, tenant-scoped by a `{tenantId}` route segment
under the new `/api/shop/{tenantId}/...` prefix.

# Context

Read `tasks/slices/026-shop-catalog.md` completely. Read B026's delivered
`src/modules/shop/TenantForge.Modules.Shop/features/products/ProductsFeature.cs`
(specifically its `LoadProductResponseAsync` helper) — this task's
product-detail endpoint follows the same variant/size-guide-loading shape,
just without `Sku` and using live stock instead of a saved snapshot (there
is no snapshot in the catalog; "live" here simply means "read fresh from
`ShopProductVariant.StockQuantity` on every request," which is already
the only value that exists).

This task is the first to use the `/api/shop/{tenantId}/...` anonymous
prefix. Minimal APIs are anonymous by default unless `.RequireAuthorization()`
is called — do not call `.RequireAuthorization()` or `.AllowAnonymous()`
on any endpoint in this task.

This task only reads the tables B025 created; it defines no new table and
adds no new file under `infrastructure/`.

# Scope — every file, in order

## 1. Response records

**`src/modules/shop/TenantForge.Modules.Shop/features/storefront/StorefrontContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Storefront;

public sealed record StorefrontCategoryResponse(string Id, string Name, string Slug, int DisplayOrder);

public sealed record StorefrontCategoryListResponse(IReadOnlyList<StorefrontCategoryResponse> Categories);

public sealed record StorefrontProductSummaryResponse(
    string Id,
    string Name,
    string Slug,
    decimal EffectivePrice,
    decimal? CompareAtPrice);

public sealed record StorefrontProductListResponse(
    IReadOnlyList<StorefrontProductSummaryResponse> Products,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);

public sealed record StorefrontVariantResponse(
    string Id,
    string Color,
    string Size,
    int StockQuantity,
    decimal EffectivePrice);

public sealed record StorefrontSizeGuideColumnResponse(string Id, string Name, int DisplayOrder);

public sealed record StorefrontSizeGuideCellResponse(string ColumnId, string Value);

public sealed record StorefrontSizeGuideRowResponse(
    string SizeLabel,
    int DisplayOrder,
    IReadOnlyList<StorefrontSizeGuideCellResponse> Cells);

public sealed record StorefrontProductDetailResponse(
    string Id,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    IReadOnlyList<StorefrontVariantResponse> Variants,
    IReadOnlyList<StorefrontSizeGuideColumnResponse> SizeGuideColumns,
    IReadOnlyList<StorefrontSizeGuideRowResponse> SizeGuideRows);
```

## 2. `src/modules/shop/TenantForge.Modules.Shop/features/storefront/StorefrontCatalogFeature.cs`

```csharp
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
```

## 3. Wire the feature into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. B026 left
`MapShopModule` as:

```csharp
    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
    }
```

Change it to:

```csharp
    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
        endpoints.MapStorefrontCatalogFeature();
    }
```

Add the matching `using` at the top of the file:

```csharp
using TenantForge.Modules.Shop.Features.Storefront;
```

# If you get stuck

**Confirming an endpoint is truly anonymous.** Run the same `curl`
command with and without an `Authorization` header and confirm both get
the same `200`/body — a real bug here would be a `401`/`403` appearing
only because a shared host-level default policy caught the route.

```bash
curl http://localhost:5080/api/shop/<tenantId>/categories
```

Expected: `200 OK` with
`{"categories":[{"id":"...","name":"پیراهن","slug":"shirts","displayOrder":1}]}`
(using the category created in B026's manual test) — no `Authorization`
header sent at all.

```bash
curl http://localhost:5080/api/shop/<tenantId>/products/classic-shirt
```

Expected: `200 OK` with the full `StorefrontProductDetailResponse` shape,
including `variants` (each with live `stockQuantity` and `effectivePrice`)
and the size-guide `sizeGuideRows`/`sizeGuideColumns` — no `sku` field
anywhere in the response.

# Acceptance

- All three endpoints work without any `Authorization` header.
- Inactive categories/products never appear, even when directly requested
  by slug (`404` for an inactive product's slug, exactly like a
  nonexistent one).
- Product detail returns live `StockQuantity` per variant and the
  effective price (`PriceOverride` when set, otherwise `BasePrice`); no
  `Sku` field anywhere in the response.
- Pagination on the category-products endpoint matches the existing
  pagination response shape (same field names/casing as B026's).
- Tenant isolation: a `{tenantId}` that does not match the product/
  category's actual tenant never returns that product/category.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: active-category listing excluding inactive
rows, paginated product listing within a category, product detail by
slug (including size-guide table shape), 404 for an inactive/nonexistent
slug, and cross-tenant isolation. The full existing IAM suite continues to
pass unmodified.

Manual: the two `curl` calls under "If you get stuck" above, with no
`Authorization` header; then deactivate the product via B026's admin
`PUT` and confirm the same product-detail call now returns `404`.

# Lifecycle

Add row `B027` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B026`, and Spec link
`tasks/backend/B027-public-storefront-catalog-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
