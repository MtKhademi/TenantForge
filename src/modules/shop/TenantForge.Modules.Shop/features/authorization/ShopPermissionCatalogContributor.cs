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
        new("shop", "فروشگاه", "مدیریت دسته‌بندی‌ها، محصولات، نرخ‌های ارسال، کدهای تخفیف و هویت فروشگاه.",
        [
            new(ShopAuthorization.CatalogManagePermission, "مدیریت دسته‌بندی‌ها و محصولات", "اجازه ایجاد و ویرایش دسته‌بندی‌ها و محصولات فروشگاه.", "write"),
            new(ShopAuthorization.ShippingManagePermission, "مدیریت ارسال و تخفیف‌ها", "اجازه تعیین نرخ‌های ارسال و ایجاد یا غیرفعال‌کردن کدهای تخفیف.", "write"),
            new(ShopAuthorization.SettingsManagePermission, "مدیریت هویت فروشگاه", "اجازه ویرایش نام فروشگاه، اطلاعات پشتیبانی و صفحات سیاست‌های مشتری.", "write"),
            new(ShopAuthorization.OrdersViewPermission, "مشاهده سفارش‌ها", "اجازه مشاهده فهرست و جزئیات سفارش‌های مشتری‌ها.", "read"),
            new(ShopAuthorization.OrdersManagePermission, "مدیریت وضعیت سفارش‌ها", "اجازه تغییر وضعیت سفارش‌ها (تحویل یا لغو).", "write")
        ])
    ];
}
