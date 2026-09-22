using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TenantForge.Modules.Iam.Infrastructure;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Migrated PostgreSQL for the API-level tests. One container is started once
/// per collection; the app's Program applies migrations and seeds the platform
/// administrator on host startup, so the fixture only needs to bring the
/// database up. The seeded admin is created by whichever host starts first;
/// because seeding is idempotent, later hosts in the collection see it already
/// present and do not duplicate it.
///
/// The container is always torn down at collection end, so a fixed database
/// name does not leak data between test runs.
///
/// xunit requires a parameterless collection fixture, so the body lives in an
/// abstract base and the concrete fixtures pin their database name in a
/// parameterless constructor.
/// </summary>
public abstract class IamDbFixtureBase : IAsyncLifetime
{
    protected const string Username = "tenantforge";
    protected const string Password = "tenantforge";

    private readonly PostgreSqlContainer _postgres;

    protected IamDbFixtureBase(string databaseName)
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(databaseName)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();
    }

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    internal IamDbContext CreateContext() => new(
        new DbContextOptionsBuilder<IamDbContext>().UseNpgsql(ConnectionString).Options);
}

/// <summary>
/// The shared, main test database. Used by every API-level test class except
/// the isolation-heavy discovery tests (see <see cref="TenantDiscoveryIsolatedCollection"/>).
/// </summary>
public sealed class IamDbFixture : IamDbFixtureBase
{
    public IamDbFixture() : base("tenantforge_iam_tests")
    {
    }
}

/// <summary>
/// A dedicated PostgreSQL database for the isolation-heavy discovery tests.
/// Those tests create several accounts per case; keeping them off the shared
/// main database stops them from inflating the account count that
/// GET /api/platform/users' oldest-first 50-row page depends on in other
/// classes (e.g. the create-then-list refresh assertion in
/// UserManagementIntegrationTests).
/// </summary>
public sealed class TenantDiscoveryDbFixture : IamDbFixtureBase
{
    public TenantDiscoveryDbFixture() : base("tenantforge_iam_discovery_tests")
    {
    }
}

[CollectionDefinition(nameof(IamApiTestCollection))]
public sealed class IamApiTestCollection : ICollectionFixture<IamDbFixture>
{
}

public sealed class RolePermissionDbFixture : IamDbFixtureBase
{
    public RolePermissionDbFixture() : base("tenantforge_role_permission_tests")
    {
    }
}

public sealed class PaginationDbFixture : IamDbFixtureBase
{
    public PaginationDbFixture() : base("tenantforge_pagination_tests")
    {
    }
}

/// <summary>
/// A dedicated database for the composition-seam startup tests, which build
/// two independent hosts back-to-back against the same database to observe
/// restart idempotency. Kept off the shared main database so its seeded admin
/// never affects other classes' row-count assumptions.
/// </summary>
public sealed class CompositionSeamDbFixture : IamDbFixtureBase
{
    public CompositionSeamDbFixture() : base("tenantforge_composition_seam_tests")
    {
    }
}

[CollectionDefinition(nameof(TenantDiscoveryIsolatedCollection))]
public sealed class TenantDiscoveryIsolatedCollection : ICollectionFixture<TenantDiscoveryDbFixture>
{
}

[CollectionDefinition(nameof(RolePermissionIsolatedCollection))]
public sealed class RolePermissionIsolatedCollection : ICollectionFixture<RolePermissionDbFixture>
{
}

[CollectionDefinition(nameof(PaginationIsolatedCollection))]
public sealed class PaginationIsolatedCollection : ICollectionFixture<PaginationDbFixture>
{
}

[CollectionDefinition(nameof(CompositionSeamIsolatedCollection))]
public sealed class CompositionSeamIsolatedCollection : ICollectionFixture<CompositionSeamDbFixture>
{
}

/// <summary>
/// A dedicated database for the Shop composition-seam tests, which boot two
/// independent hosts back-to-back against the same database to observe
/// restart idempotency. Kept off the shared main database so its shop tables
/// never affect other classes' assumptions.
/// </summary>
public sealed class ShopDbFixture : IamDbFixtureBase
{
    public ShopDbFixture() : base("tenantforge_shop_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopIsolatedCollection))]
public sealed class ShopIsolatedCollection : ICollectionFixture<ShopDbFixture>
{
}

/// <summary>
/// A dedicated database for B026's catalog-admin endpoint tests. Kept off the
/// shared main database (so IAM row-count assumptions stay stable) and off the
/// B025 composition-seam database (so those table/history assertions stay
/// stable).
/// </summary>
public sealed class ShopAdminDbFixture : IamDbFixtureBase
{
    public ShopAdminDbFixture() : base("tenantforge_shop_admin_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopAdminIsolatedCollection))]
public sealed class ShopAdminIsolatedCollection : ICollectionFixture<ShopAdminDbFixture>
{
}

/// <summary>
/// A dedicated database for B027's anonymous storefront read tests. Kept off the
/// shared main database (so IAM row-count assumptions stay stable) and off the
/// B026 catalog-admin database (so those row-count/pagination assertions stay
/// stable). The tests author data through B026's authenticated admin API and
/// read it back through the new anonymous storefront endpoints.
/// </summary>
public sealed class ShopStorefrontDbFixture : IamDbFixtureBase
{
    public ShopStorefrontDbFixture() : base("tenantforge_shop_storefront_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopStorefrontIsolatedCollection))]
public sealed class ShopStorefrontIsolatedCollection : ICollectionFixture<ShopStorefrontDbFixture>
{
}

/// <summary>
/// A dedicated database for B029's tenant-scoped shipping-rate and coupon
/// admin tests. Kept off the shared main database (so IAM row-count
/// assumptions stay stable) and off the other Shop test databases (so their
/// row-count assertions stay stable). The tests run as a real tenant owner's
/// JWT through B026's ShopAuthorization raw-SQL membership check.
/// </summary>
public sealed class ShopShippingCouponDbFixture : IamDbFixtureBase
{
    public ShopShippingCouponDbFixture() : base("tenantforge_shop_shipping_coupon_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopShippingCouponIsolatedCollection))]
public sealed class ShopShippingCouponIsolatedCollection : ICollectionFixture<ShopShippingCouponDbFixture>
{
}

/// <summary>
/// A dedicated database for B028's anonymous cart tests. Kept off the shared
/// main database (so IAM row-count assumptions stay stable) and off the B026
/// admin / B027 storefront databases (so their row-count and catalog
/// assertions stay stable). The tests author catalog data through B026's
/// authenticated admin API, then exercise the new cart endpoints with a bare
/// client that sends no Authorization header.
/// </summary>
public sealed class ShopCartDbFixture : IamDbFixtureBase
{
    public ShopCartDbFixture() : base("tenantforge_shop_cart_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopCartIsolatedCollection))]
public sealed class ShopCartIsolatedCollection : ICollectionFixture<ShopCartDbFixture>
{
}

/// <summary>
/// A dedicated database for B030's anonymous checkout-summary tests. Kept off
/// the shared main database (so IAM row-count assumptions stay stable) and off
/// the B026 admin / B027 storefront / B028 cart / B029 shipping-coupon
/// databases (so their row-count and catalog assertions stay stable). The
/// tests author catalog, shipping-rate and coupon data through B026/B029's
/// authenticated admin APIs, build a cart through B028's anonymous API, then
/// drive the checkout-summary endpoint with a bare client that sends no
/// Authorization header.
/// </summary>
public sealed class ShopCheckoutDbFixture : IamDbFixtureBase
{
    public ShopCheckoutDbFixture() : base("tenantforge_shop_checkout_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopCheckoutIsolatedCollection))]
public sealed class ShopCheckoutIsolatedCollection : ICollectionFixture<ShopCheckoutDbFixture>
{
}

/// <summary>
/// A dedicated database for B031's anonymous order-creation tests. Kept off
/// the shared main database (so IAM row-count assumptions stay stable) and off
/// the B026 admin / B027 storefront / B028 cart / B029 shipping-coupon / B030
/// checkout databases (so their row-count and catalog assertions stay stable).
/// The tests author catalog, shipping-rate and coupon data through
/// B026/B029's authenticated admin APIs, build a cart through B028's
/// anonymous API, then drive the order-creation endpoint with a bare client
/// that sends no Authorization header.
/// </summary>
public sealed class ShopOrderDbFixture : IamDbFixtureBase
{
    public ShopOrderDbFixture() : base("tenantforge_shop_order_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopOrderIsolatedCollection))]
public sealed class ShopOrderIsolatedCollection : ICollectionFixture<ShopOrderDbFixture>
{
}

/// <summary>
/// A dedicated database for B032's anonymous sandbox-payment tests. Kept off
/// the shared main database (so IAM row-count assumptions stay stable) and off
/// the B026 admin / B027 storefront / B028 cart / B029 shipping-coupon / B030
/// checkout / B031 order databases (so their row-count and catalog assertions
/// stay stable). The tests author catalog data through B026's authenticated
/// admin API, create an order through B031's anonymous API, then drive the
/// payment initiate/callback endpoints with a bare client that sends no
/// Authorization header.
/// </summary>
public sealed class ShopPaymentDbFixture : IamDbFixtureBase
{
    public ShopPaymentDbFixture() : base("tenantforge_shop_payment_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopPaymentIsolatedCollection))]
public sealed class ShopPaymentIsolatedCollection : ICollectionFixture<ShopPaymentDbFixture>
{
}

/// <summary>
/// A dedicated database for B033's anonymous order-lookup tests. Kept off the
/// shared main database (so IAM row-count assumptions stay stable) and off the
/// B026 admin / B027 storefront / B028 cart / B029 shipping-coupon / B030
/// checkout / B031 order / B032 payment databases (so their row-count and
/// catalog assertions stay stable). The tests author catalog data through
/// B026's authenticated admin API, create an order through B031's anonymous
/// API (and pay it through B032's), then drive the order-lookup endpoint with
/// a bare client that sends no Authorization header.
/// </summary>
public sealed class ShopOrderLookupDbFixture : IamDbFixtureBase
{
    public ShopOrderLookupDbFixture() : base("tenantforge_shop_order_lookup_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopOrderLookupIsolatedCollection))]
public sealed class ShopOrderLookupIsolatedCollection : ICollectionFixture<ShopOrderLookupDbFixture>
{
}

/// <summary>
/// A dedicated database for B036's product-gallery persistence and media
/// serving tests. Kept off prior Shop databases so gallery row counts and
/// filesystem side effects stay isolated.
/// </summary>
public sealed class ShopProductMediaDbFixture : IamDbFixtureBase
{
    public ShopProductMediaDbFixture() : base("tenantforge_shop_product_media_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopProductMediaIsolatedCollection))]
public sealed class ShopProductMediaIsolatedCollection : ICollectionFixture<ShopProductMediaDbFixture>
{
}

/// <summary>
/// A dedicated database for B038's category-hierarchy tests (admin
/// create/update with parents, effective public activity, nested public
/// list, root/child product filtering, and the pre-B038 flat-row migration
/// shape). Kept off prior Shop databases so their row-count assertions stay
/// stable.
/// </summary>
public sealed class ShopCategoryHierarchyDbFixture : IamDbFixtureBase
{
    public ShopCategoryHierarchyDbFixture() : base("tenantforge_shop_category_hierarchy_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopCategoryHierarchyIsolatedCollection))]
public sealed class ShopCategoryHierarchyIsolatedCollection : ICollectionFixture<ShopCategoryHierarchyDbFixture>
{
}

/// <summary>
/// A dedicated database for B039's tenant storefront profile/policy tests
/// (admin create/update with optimistic concurrency, the unique-tenant race,
/// permission enforcement and the anonymous public read). Kept off prior Shop
/// databases so their row-count assumptions stay stable.
/// </summary>
public sealed class ShopProfileDbFixture : IamDbFixtureBase
{
    public ShopProfileDbFixture() : base("tenantforge_shop_profile_tests")
    {
    }
}

[CollectionDefinition(nameof(ShopProfileIsolatedCollection))]
public sealed class ShopProfileIsolatedCollection : ICollectionFixture<ShopProfileDbFixture>
{
}
