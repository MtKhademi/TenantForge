using System.Linq.Expressions;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Features.Categories;

/// <summary>
/// B038: the single "effective public activity" rule for categories. A
/// category is publicly visible only while it is active AND, when it has a
/// parent (i.e. it is a direct child), its root parent is active too. Every
/// public storefront read (public category list, products-by-category, the
/// all-products category join, product detail, public media bytes) applies
/// this same predicate, so a deactivated root hides its children everywhere
/// at once instead of each endpoint duplicating a slightly different check.
///
/// <see cref="For"/> builds the rule as a translatable expression tree: the
/// queryable is captured in the closure (the same mechanism EF Core already
/// uses for the module's <c>db.Categories.Any(...)</c> subquery closures),
/// so it compiles into one SQL query with a correlated EXISTS.
/// </summary>
internal static class CategoryVisibility
{
    public static Expression<Func<ShopCategory, bool>> For(IQueryable<ShopCategory> allCategories)
    {
        var all = allCategories;
        return category => category.IsActive
            && (category.ParentCategoryId == null
                || all.Any(parent => parent.Id == category.ParentCategoryId && parent.IsActive));
    }
}
