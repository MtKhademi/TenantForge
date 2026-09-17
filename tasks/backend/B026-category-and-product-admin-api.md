---
id: B026
slice: S26
title: Category and product admin API
agent: backend-mentor
source: tasks/slices/026-shop-catalog.md
---

# Objective

Give an authenticated tenant member (a boutique owner/admin) tenant-scoped
endpoints to create/list/update categories, and to create/list/update one
product together with all of its color/size variants and its size-guide
table in a single authoring payload. No public/anonymous endpoint is added
in this task.

This Spec gives you every file's exact path, exact request/response
types and the exact endpoint code. Follow it literally.

# Context

Read `tasks/slices/026-shop-catalog.md` completely. Read B025's delivered
`src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopDbContext.cs`
and `ShopModule.cs` before editing them (this task adds to both).

Real IAM files this task's code is modeled on:
- `src/modules/iam/TenantForge.Modules.Iam/features/users/UsersFeature.cs`
  — the minimal-API endpoint-mapping and validation style (a static
  `Map<Feature>` extension method returning `IEndpointRouteBuilder`,
  a `Dictionary<string, string[]>` of field errors returned via
  `Results.ValidationProblem`, `Results.Created` for a successful POST).
- `src/modules/iam/TenantForge.Modules.Iam/features/tenantmembers/TenantMembersFeature.cs`
  — reading the caller's account id from the JWT `sub` claim and parsing
  the `{tenantId}` route value as a TSID before doing anything else.
- `src/modules/iam/TenantForge.Modules.Iam/features/pagination/PaginationSupport.cs`
  — the exact pagination-binding shape this task's Shop-owned copy below
  reproduces.

## Why this task cannot call IAM's tenant-membership check directly

`TenantForge.Modules.Shop` has no `ProjectReference` to
`TenantForge.Modules.Iam` and never will (modules depend only on
`TenantForge.BuildingBlocks` and their own Contract project — B018/S20).
So this task cannot call `IamDbContext.TenantMemberships` in C#. B025's
Context section already decided the fix: `Shop:ShopDb` points at the same
physical database as `IAM:IamDb`, so this task's `ShopAuthorization`
helper below runs a **raw SQL** query against IAM's own
`iam_tenant_memberships`/`iam_accounts`/`iam_tenants` tables. Copy the SQL
exactly as written — do not add an EF entity for any `iam_*` table.

# Scope — every file, in order

## 1. Shop's own pagination helper

`TenantForge.Modules.Shop` needs its own copy of IAM's pagination types
(it cannot reference IAM's Contract project either). Create these two
files, copied field-for-field from IAM's real files:

**`src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationQuery.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Pagination;

public sealed record PaginationQuery(int PageNumber, int PageSize)
{
    public int Offset => checked((PageNumber - 1) * PageSize);
}
```

**`src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationMetadata.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Pagination;

public sealed record PaginationMetadata(int PageNumber, int PageSize, int TotalCount, int TotalPages, bool HasPreviousPage, bool HasNextPage)
{
    public static PaginationMetadata From(PaginationQuery query, int totalCount)
    {
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
        return new(query.PageNumber, query.PageSize, totalCount, totalPages, query.PageNumber > 1, query.PageNumber < totalPages);
    }
}
```

**`src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationSupport.cs`:**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace TenantForge.Modules.Shop.Features.Pagination;

internal static class PaginationSupport
{
    private const int DefaultPageNumber = 1;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static bool TryBind(HttpRequest request, out PaginationQuery query, out Dictionary<string, string[]> errors)
    {
        errors = new(StringComparer.OrdinalIgnoreCase);
        var pageNumber = Parse(request, "pageNumber", DefaultPageNumber, int.MaxValue, errors);
        var pageSize = Parse(request, "pageSize", DefaultPageSize, MaxPageSize, errors);

        if (errors.Count > 0)
        {
            query = new(DefaultPageNumber, DefaultPageSize);
            return false;
        }

        try
        {
            _ = checked((pageNumber - 1) * pageSize);
        }
        catch (OverflowException)
        {
            errors["pageNumber"] = ["The requested page is too large."];
            query = new(DefaultPageNumber, DefaultPageSize);
            return false;
        }

        query = new(pageNumber, pageSize);
        return true;
    }

    public static async Task<(IReadOnlyList<T> Items, PaginationMetadata Pagination)> PageAsync<T>(IQueryable<T> queryable, PaginationQuery query)
    {
        var totalCount = await queryable.CountAsync();
        var items = await queryable.Skip(query.Offset).Take(query.PageSize).ToListAsync();
        return (items, PaginationMetadata.From(query, totalCount));
    }

    private static int Parse(HttpRequest request, string field, int defaultValue, int maxValue, Dictionary<string, string[]> errors)
    {
        if (!request.Query.TryGetValue(field, out var values)) return defaultValue;
        var value = values.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{field} must be a number."];
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 1 || parsed > maxValue)
        {
            errors[field] = field == "pageSize"
                ? [$"pageSize must be between 1 and {MaxPageSize}."]
                : ["pageNumber must be 1 or greater."];
            return defaultValue;
        }

        return parsed;
    }
}
```

## 2. `src/modules/shop/TenantForge.Modules.Shop/features/authorization/ShopAuthorization.cs`

Every later admin task (B029) reuses this file as-is — do not duplicate
its SQL elsewhere.

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TSID.Creator.NET;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Infrastructure;

namespace TenantForge.Modules.Shop.Features.Authorization;

/// <summary>
/// The result of checking whether the caller may act on a tenant's Shop
/// data. Mirrors the shape of TenantForge.Modules.Iam.Features.Roles.TenantAccess
/// (a Result slot the caller returns directly when non-null).
/// </summary>
internal sealed record ShopTenantAccess(Tsid TenantId, Tsid AccountId, IResult? Result)
{
    public static ShopTenantAccess Forbidden { get; } = new(default, default, Results.Forbid());
}

internal static class ShopAuthorization
{
    /// <summary>
    /// Parses the route's tenantId, reads the caller's account id from the
    /// JWT "sub" claim, then checks IAM's own iam_tenant_memberships table
    /// with a raw SQL query — see B026's Spec Context for why this cannot be
    /// an EF entity/DbSet reference. Requires an active membership for an
    /// active account in an active tenant, mirroring
    /// RolesFeature.AuthorizeTenantAccessAsync's own active/active/active
    /// join exactly.
    /// </summary>
    public static async Task<ShopTenantAccess> AuthorizeTenantAccessAsync(
        string tenantId,
        ClaimsPrincipal principal,
        ShopDbContext db)
    {
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return ShopTenantAccess.Forbidden;
        }

        if (!TsidId.TryParse(tenantId, out var tenantTsid))
        {
            return ShopTenantAccess.Forbidden;
        }

        var accountTsid = TsidId.TryParseNullable(principal.FindFirstValue("sub"));
        if (accountTsid is null)
        {
            return ShopTenantAccess.Forbidden;
        }

        var membershipCount = await db.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*)::int AS "Value"
            FROM iam_tenant_memberships m
            JOIN iam_accounts a ON a.id = m.account_id AND a.status = 'Active'
            JOIN iam_tenants t ON t.id = m.tenant_id AND t.status = 'Active'
            WHERE m.tenant_id = {0} AND m.account_id = {1}
            """,
            tenantTsid.ToLong(),
            accountTsid.Value.ToLong())
            .SingleAsync();

        if (membershipCount == 0)
        {
            return ShopTenantAccess.Forbidden;
        }

        return new ShopTenantAccess(tenantTsid, accountTsid.Value, null);
    }
}
```

## 3. Request/response records

Create these two files as plain `public sealed record` types (no
`TenantForge.Modules.Shop.Contract` project exists yet — do not create
one).

**`src/modules/shop/TenantForge.Modules.Shop/features/categories/CategoryContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Categories;

public sealed record CreateCategoryRequest(string? Name, string? Slug, int DisplayOrder);

public sealed record UpdateCategoryRequest(string? Name, string? Slug, int DisplayOrder, bool IsActive);

public sealed record CategoryResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    int DisplayOrder,
    bool IsActive);

public sealed record CategoryListResponse(
    IReadOnlyList<CategoryResponse> Categories,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
```

**`src/modules/shop/TenantForge.Modules.Shop/features/products/ProductContracts.cs`:**

```csharp
namespace TenantForge.Modules.Shop.Features.Products;

public sealed record ProductVariantInput(string? Color, string? Size, string? Sku, int StockQuantity, decimal? PriceOverride);

public sealed record SizeGuideRowInput(string? SizeLabel, IReadOnlyList<string>? Values);

public sealed record CreateProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    string? CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    IReadOnlyList<ProductVariantInput>? Variants,
    IReadOnlyList<string>? SizeGuideColumns,
    IReadOnlyList<SizeGuideRowInput>? SizeGuideRows);

public sealed record UpdateProductRequest(
    string? Name,
    string? Slug,
    string? Description,
    string? CategoryId,
    decimal BasePrice,
    decimal? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ProductVariantInput>? Variants,
    IReadOnlyList<string>? SizeGuideColumns,
    IReadOnlyList<SizeGuideRowInput>? SizeGuideRows);

public sealed record ProductVariantResponse(
    string Id,
    string Color,
    string Size,
    string Sku,
    int StockQuantity,
    decimal? PriceOverride);

public sealed record SizeGuideColumnResponse(string Id, string Name, int DisplayOrder);

public sealed record SizeGuideCellResponse(string ColumnId, string Value);

public sealed record SizeGuideRowResponse(
    string Id,
    string SizeLabel,
    int DisplayOrder,
    IReadOnlyList<SizeGuideCellResponse> Cells);

public sealed record ProductResponse(
    string Id,
    string TenantId,
    string CategoryId,
    string Name,
    string Slug,
    string Description,
    decimal BasePrice,
    decimal? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ProductVariantResponse> Variants,
    IReadOnlyList<SizeGuideColumnResponse> SizeGuideColumns,
    IReadOnlyList<SizeGuideRowResponse> SizeGuideRows);

public sealed record ProductSummaryResponse(
    string Id,
    string Name,
    string Slug,
    string CategoryId,
    decimal BasePrice,
    bool IsActive,
    int VariantCount);

public sealed record ProductListResponse(
    IReadOnlyList<ProductSummaryResponse> Products,
    TenantForge.Modules.Shop.Features.Pagination.PaginationMetadata Pagination);
```

## 4. `src/modules/shop/TenantForge.Modules.Shop/features/categories/CategoriesFeature.cs`

```csharp
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

namespace TenantForge.Modules.Shop.Features.Categories;

internal static class CategoriesFeature
{
    public static IEndpointRouteBuilder MapCategoriesFeature(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenants/{tenantId}/shop/categories", async (
            string tenantId,
            CreateCategoryRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            var errors = ValidateCategoryFields(request.Name, request.Slug);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var slug = request.Slug!.Trim().ToLowerInvariant();
            var duplicate = await db.Categories.AnyAsync(category =>
                category.TenantId == access.TenantId && category.Slug == slug);
            if (duplicate) return DuplicateSlugProblem();

            var category = ShopCategory.Create(access.TenantId, request.Name!, slug, request.DisplayOrder);
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            var response = ToResponse(category);
            return Results.Created($"/api/tenants/{tenantId}/shop/categories/{TsidId.Format(category.Id)}", response);
        }).RequireAuthorization();

        endpoints.MapGet("/api/tenants/{tenantId}/shop/categories", async (
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

            var query = db.Categories.AsNoTracking()
                .Where(category => category.TenantId == access.TenantId)
                .OrderBy(category => category.DisplayOrder)
                .ThenBy(category => category.Id);

            var (categories, pagination) = await PaginationSupport.PageAsync(query, page);
            return Results.Ok(new CategoryListResponse(categories.Select(ToResponse).ToList(), pagination));
        }).RequireAuthorization();

        endpoints.MapPut("/api/tenants/{tenantId}/shop/categories/{categoryId}", async (
            string tenantId,
            string categoryId,
            UpdateCategoryRequest request,
            ClaimsPrincipal principal,
            ShopDbContext db) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(categoryId, out var categoryTsid))
            {
                return Results.NotFound();
            }

            var errors = ValidateCategoryFields(request.Name, request.Slug);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var category = await db.Categories.SingleOrDefaultAsync(category =>
                category.TenantId == access.TenantId && category.Id == categoryTsid);
            if (category is null) return Results.NotFound();

            var slug = request.Slug!.Trim().ToLowerInvariant();
            var duplicate = await db.Categories.AnyAsync(other =>
                other.TenantId == access.TenantId && other.Slug == slug && other.Id != categoryTsid);
            if (duplicate) return DuplicateSlugProblem();

            category.Update(request.Name!, slug, request.DisplayOrder, request.IsActive);
            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(category));
        }).RequireAuthorization();

        return endpoints;
    }

    private static Dictionary<string, string[]> ValidateCategoryFields(string? name, string? slug)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Category name is required."];
        }
        else if (name.Trim().Length > 120)
        {
            errors["name"] = ["Category name must be 120 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            errors["slug"] = ["Category slug is required."];
        }
        else if (slug.Trim().Length > 120)
        {
            errors["slug"] = ["Category slug must be 120 characters or fewer."];
        }

        return errors;
    }

    private static IResult DuplicateSlugProblem() => Results.Problem(
        title: "Duplicate category slug",
        detail: "A category with this slug already exists in this tenant.",
        statusCode: StatusCodes.Status409Conflict);

    private static CategoryResponse ToResponse(ShopCategory category) => new(
        TsidId.Format(category.Id),
        TsidId.Format(category.TenantId),
        category.Name,
        category.Slug,
        category.DisplayOrder,
        category.IsActive);
}
```

## 5. `src/modules/shop/TenantForge.Modules.Shop/features/products/ProductsFeature.cs`

```csharp
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
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
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
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db);
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
                && (existingProductId is null || product.Id != existingProductId));
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
```

## 6. Wire the two features into the composition seam

Edit `src/modules/shop/TenantForge.Modules.Shop/ShopModule.cs`. B025 left
`UseShopModuleAsync` with no endpoint mapping. It currently reads:

```csharp
    public static async Task UseShopModuleAsync(this WebApplication app)
    {
        ValidateShopModuleConfiguration(app.Environment, app.Configuration);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.MigrateAsync();
    }
```

Change it to add a `MapShopModule` call, the same pattern
`IamModule.MapIamModule` uses:

```csharp
    public static async Task UseShopModuleAsync(this WebApplication app)
    {
        ValidateShopModuleConfiguration(app.Environment, app.Configuration);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Database.MigrateAsync();

        MapShopModule(app);
    }

    private static void MapShopModule(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCategoriesFeature();
        endpoints.MapProductsFeature();
    }
```

Add the two matching `using` statements at the top of the file:

```csharp
using Microsoft.AspNetCore.Routing;
using TenantForge.Modules.Shop.Features.Categories;
using TenantForge.Modules.Shop.Features.Products;
```

# If you get stuck

**Testing the combined product-authoring payload manually.** After
`dotnet run --project src/api/TenantForge.Api`, sign in as a seeded tenant
member (`POST /api/auth/login` with the seeded admin credentials from
`appsettings.Development.json`) to get an `accessToken`, then:

```bash
curl -X POST http://localhost:5080/api/tenants/<tenantId>/shop/categories \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"name":"پیراهن","slug":"shirts","displayOrder":1}'
```

Expected: `201 Created` with a body like
`{"id":"...","tenantId":"...","name":"پیراهن","slug":"shirts","displayOrder":1,"isActive":true}`.
Copy the returned category `id` into the next call:

```bash
curl -X POST http://localhost:5080/api/tenants/<tenantId>/shop/products \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{
        "name": "پیراهن کلاسیک",
        "slug": "classic-shirt",
        "description": "پیراهن نخی کلاسیک",
        "categoryId": "<categoryId>",
        "basePrice": 890000,
        "compareAtPrice": null,
        "variants": [
          {"color": "سفید", "size": "M", "sku": "SHIRT-WHT-M", "stockQuantity": 10, "priceOverride": null},
          {"color": "سفید", "size": "L", "sku": "SHIRT-WHT-L", "stockQuantity": 5, "priceOverride": null}
        ],
        "sizeGuideColumns": ["دور سینه", "دور کمر"],
        "sizeGuideRows": [
          {"sizeLabel": "M", "values": ["96", "80"]},
          {"sizeLabel": "L", "values": ["102", "86"]}
        ]
      }'
```

Expected: `201 Created` with the full `ProductResponse` shape, including
`variants` (2 entries) and `sizeGuideColumns`/`sizeGuideRows` (2 columns,
2 rows, each row's `cells` carrying one value per column in the same
order).

# Acceptance

- All six endpoints above exist, tenant-scoped, requiring
  `ShopAuthorization.AuthorizeTenantAccessAsync` to succeed.
- Creating a product with variants and a size guide in one request
  persists all rows correctly and returns them in the response; fetching
  the same product by id returns an identical shape.
- Duplicate `Slug` (category or product) within the same tenant is
  rejected with a clear validation error; the same slug in two different
  tenants is allowed.
- A size-guide row with the wrong number of values is rejected with a
  clear validation error, not silently truncated/padded.
- Updating a product replaces its variants and size-guide rows correctly
  (old rows removed, new rows present).
- A caller who is not a member of `{tenantId}` gets `403`; an
  unauthenticated caller gets `401`.

# Verification

Automated:

```bash
dotnet build TenantForge.sln --nologo
dotnet test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

New integration tests cover: category create/list/update, product
create-with-variants-and-size-guide, product fetch-by-id, product update
replacing variants, duplicate-slug rejection, and the 401/403 authorization
cases. The full existing IAM suite continues to pass unmodified.

Manual: the two `curl` calls under "If you get stuck" above, run against
a locally running API with a seeded tenant member's token; also confirm a
token for an account with no membership in that tenant gets `403` on the
same calls.

# Lifecycle

Add row `B026` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B025`, and Spec link
`tasks/backend/B026-category-and-product-admin-api.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
