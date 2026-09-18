using TenantForge.BuildingBlocks.Permissions;

namespace TenantForge.Modules.Shop.Features.Authorization;

/// <summary>
/// Shop's own contribution to the shared permission catalog (B034/B035) —
/// the second real consumer of the BuildingBlocks contributor/aggregator
/// seam, exactly as B034's admission evidence recorded.
/// </summary>
internal sealed class ShopPermissionCatalogContributor : IPermissionCatalogContributor
{
    public IReadOnlyList<PermissionGroup> GetPermissionGroups() =>
    [
        new("shop", "فروشگاه", "مدیریت دسته‌بندی‌ها، محصولات، نرخ‌های ارسال و کدهای تخفیف فروشگاه.",
        [
            new(ShopAuthorization.CatalogManagePermission, "مدیریت دسته‌بندی‌ها و محصولات", "اجازه ایجاد و ویرایش دسته‌بندی‌ها و محصولات فروشگاه.", "write"),
            new(ShopAuthorization.ShippingManagePermission, "مدیریت ارسال و تخفیف‌ها", "اجازه تعیین نرخ‌های ارسال و ایجاد یا غیرفعال‌کردن کدهای تخفیف.", "write")
        ])
    ];
}
