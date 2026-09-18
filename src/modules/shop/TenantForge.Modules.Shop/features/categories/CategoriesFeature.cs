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
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
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
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
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
