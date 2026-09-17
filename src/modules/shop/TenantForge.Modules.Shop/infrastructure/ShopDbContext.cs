using Microsoft.EntityFrameworkCore;
using TenantForge.Modules.Shop.Domain;

namespace TenantForge.Modules.Shop.Infrastructure;

internal sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    internal DbSet<ShopCategory> Categories => Set<ShopCategory>();
    internal DbSet<ShopProduct> Products => Set<ShopProduct>();
    internal DbSet<ShopProductVariant> ProductVariants => Set<ShopProductVariant>();
    internal DbSet<ShopSizeGuideColumn> SizeGuideColumns => Set<ShopSizeGuideColumn>();
    internal DbSet<ShopSizeGuideRow> SizeGuideRows => Set<ShopSizeGuideRow>();
    internal DbSet<ShopSizeGuideCell> SizeGuideCells => Set<ShopSizeGuideCell>();
    internal DbSet<ShopShippingRate> ShippingRates => Set<ShopShippingRate>();
    internal DbSet<ShopCoupon> Coupons => Set<ShopCoupon>();
    internal DbSet<ShopCart> Carts => Set<ShopCart>();
    internal DbSet<ShopCartItem> CartItems => Set<ShopCartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopCategoryMap());
        modelBuilder.ApplyConfiguration(new ShopProductMap());
        modelBuilder.ApplyConfiguration(new ShopProductVariantMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideColumnMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideRowMap());
        modelBuilder.ApplyConfiguration(new ShopSizeGuideCellMap());
        modelBuilder.ApplyConfiguration(new ShopShippingRateMap());
        modelBuilder.ApplyConfiguration(new ShopCouponMap());
        modelBuilder.ApplyConfiguration(new ShopCartMap());
        modelBuilder.ApplyConfiguration(new ShopCartItemMap());
    }
}
