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
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;

            var errors = ValidateCategoryFields(request.Name, request.Slug);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var slug = request.Slug!.Trim().ToLowerInvariant();
            Tsid? parentTsid = null;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var duplicate = await db.Categories.AnyAsync(category =>
                    category.TenantId == access.TenantId && category.Slug == slug, ct);
                if (duplicate) return DuplicateSlugProblem();

                // B038: resolve + lock the chosen parent so a concurrent
                // re-parenting of that root cannot turn it into a child between
                // this check and our insert (LockCategoryAsync below).
                if (!string.IsNullOrWhiteSpace(request.ParentCategoryId))
                {
                    if (!TsidId.TryParse(request.ParentCategoryId, out var parsedParent))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["parentCategoryId"] = ["Select an active root category."]
                        });
                    }

                    parentTsid = parsedParent;
                    var parentProblem = await ValidateCreateParentAsync(db, access.TenantId, parsedParent, ct);
                    if (parentProblem is not null) return parentProblem;
                }

                var category = ShopCategory.Create(access.TenantId, request.Name!, slug, request.DisplayOrder, parentTsid);
                db.Categories.Add(category);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var response = ToResponse(category);
                return Results.Created($"/api/tenants/{tenantId}/shop/categories/{TsidId.Format(category.Id)}", response);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
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
            ShopDbContext db,
            CancellationToken ct) =>
        {
            var access = await ShopAuthorization.AuthorizeTenantAccessAsync(tenantId, principal, db, ShopAuthorization.CatalogManagePermission);
            if (access.Result is not null) return access.Result;

            if (!TsidId.TryParse(categoryId, out var categoryTsid))
            {
                return Results.NotFound();
            }

            var errors = ValidateCategoryFields(request.Name, request.Slug);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            Tsid? parentTsid = null;
            if (!string.IsNullOrWhiteSpace(request.ParentCategoryId))
            {
                if (!TsidId.TryParse(request.ParentCategoryId, out var parsedParent))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["parentCategoryId"] = ["Select an active root category."]
                    });
                }

                parentTsid = parsedParent;
            }

            var slug = request.Slug!.Trim().ToLowerInvariant();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // B038: lock the category row being modified so the "does it
                // have children?" reparent guard and the write are atomic with
                // respect to a concurrent child-creation under this category.
                var category = await LockCategoryAsync(db, access.TenantId, categoryTsid, ct);
                if (category is null) return Results.NotFound();

                var duplicate = await db.Categories.AnyAsync(other =>
                    other.TenantId == access.TenantId && other.Slug == slug && other.Id != categoryTsid, ct);
                if (duplicate) return DuplicateSlugProblem();

                // A parent that already has children can never become a child
                // (that would create a third level). This is the named race the
                // row lock above closes: the AnyAsync and the SaveChanges run
                // under the same lock on this row.
                if (parentTsid is not null && category.ParentCategoryId != parentTsid)
                {
                    var hasChildren = await db.Categories.AnyAsync(child => child.ParentCategoryId == categoryTsid, ct);
                    if (hasChildren) return ReparentConflictProblem();
                }

                var parentProblem = await ValidateParentAsync(db, access.TenantId, categoryTsid, parentTsid, ct);
                if (parentProblem is not null) return parentProblem;

                category.Update(request.Name!, slug, request.DisplayOrder, request.IsActive, parentTsid);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return Results.Ok(ToResponse(category));
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// B038: the Spec's parent-eligibility rule, used verbatim. A supplied
    /// parent must exist in the same tenant, be active, itself have no parent
    /// (max depth is root + one direct child), and not be the category's own
    /// id. Any violation returns the same <c>parentCategoryId</c> field error.
    /// </summary>
    private static async Task<IResult?> ValidateParentAsync(
        ShopDbContext db, Tsid tenantId, Tsid categoryId, Tsid? parentId, CancellationToken ct)
    {
        if (parentId is null) return null;
        var parent = await db.Categories.SingleOrDefaultAsync(
            c => c.Id == parentId && c.TenantId == tenantId, ct);
        if (parent is null || !parent.IsActive || parent.ParentCategoryId is not null || parent.Id == categoryId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentCategoryId"] = ["Select an active root category."] });
        return null;
    }

    /// <summary>
    /// B038 (create): locks the candidate parent row (FOR UPDATE) so a
    /// concurrent re-parenting of that root is serialized against our child
    /// insert, then applies the Spec's eligibility rule against the locked row
    /// (the self-parent branch cannot fire on create — the child does not
    /// exist yet).
    /// </summary>
    private static async Task<IResult?> ValidateCreateParentAsync(ShopDbContext db, Tsid tenantId, Tsid parentId, CancellationToken ct)
    {
        var parent = await LockCategoryAsync(db, tenantId, parentId, ct);
        if (parent is null || !parent.IsActive || parent.ParentCategoryId is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["parentCategoryId"] = ["Select an active root category."]
            });
        }

        return null;
    }

    /// <summary>
    /// B038: acquires a PostgreSQL row lock (SELECT ... FOR UPDATE) on one
    /// category within the caller's ambient transaction, so the guard + write
    /// are atomic. Uses the raw column values (the converter's bigint backing)
    /// because the lock must be expressed in SQL, not LINQ.
    /// </summary>
    private static Task<ShopCategory?> LockCategoryAsync(ShopDbContext db, Tsid tenantId, Tsid categoryId, CancellationToken ct) =>
        db.Categories
            .FromSqlRaw(
                "SELECT * FROM shop_categories WHERE tenant_id = {0} AND id = {1} FOR UPDATE",
                tenantId.ToLong(), categoryId.ToLong())
            .SingleOrDefaultAsync(ct);

    private static IResult DuplicateSlugProblem() => Results.Problem(
        title: "Duplicate category slug",
        detail: "A category with this slug already exists in this tenant.",
        statusCode: StatusCodes.Status409Conflict);

    private static IResult ReparentConflictProblem() => Results.Problem(
        title: "Reparent conflict",
        detail: "A category that already has children cannot be moved under another category.",
        statusCode: StatusCodes.Status409Conflict);

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

    private static CategoryResponse ToResponse(ShopCategory category) => new(
        TsidId.Format(category.Id),
        TsidId.Format(category.TenantId),
        category.Name,
        category.Slug,
        category.DisplayOrder,
        category.IsActive,
        category.ParentCategoryId is { } parent ? TsidId.Format(parent) : null);
}
