using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TenantForge.BuildingBlocks.Identifiers;
using TenantForge.Modules.Shop.Domain;
using TenantForge.Modules.Shop.Infrastructure;
using TSID.Creator.NET;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Isolated migration coverage for B041: the shared API fixture always migrates
/// to the latest version before a test can insert a representative pre-migration
/// coupon row, so this class boots its own Postgres container, applies only the
/// migrations up to the one immediately before AddShopCouponRules, inserts a
/// legacy shop_coupons row (no minimum_subtotal / maximum_discount_amount /
/// redemption_limit / redeemed_count / version columns yet), then applies the
/// remaining migrations and asserts the row was backfilled to the Spec's
/// defaults (unlimited, zero redeemed, version zero) and is still usable.
/// Mirrors PermissionKeyMigrationTests.
/// </summary>
public sealed class ShopCouponRulesMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenantforge_shop_coupon_migration_tests")
        .WithUsername("tenantforge")
        .WithPassword("tenantforge")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task PreB041CouponRows_MigrateToUnlimitedAndZeroRedeemed_AndRemainUsable()
    {
        var tenantId = TsidId.NewId();
        var couponId = TsidId.NewId();

        await using (var context = CreateContext())
        {
            // Apply everything up to the migration immediately before B041's,
            // so shop_coupons exists but its five new columns do not.
            await context.GetInfrastructure().GetRequiredService<IMigrator>()
                .MigrateAsync("20260922122036_AddShopCartReservationExpiry");

            // Insert a coupon the way pre-B041 data looked: none of the new
            // columns present (they do not exist at this migration point, so a
            // select on them would fail — confirming we are truly pre-B041).
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO shop_coupons
                    (id, tenant_id, code, normalized_code, discount_type, discount_value, is_active, expires_at_utc)
                VALUES ({0}, {1}, 'LEGACY10', 'LEGACY10', 'Percentage', 10, true, NULL);
                """,
                couponId.ToLong(), tenantId.ToLong());

            // Confirm the legacy row is present using a pre-existing column only.
            await using var connection = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "select discount_value from shop_coupons where id = @id;";
            command.Parameters.Add(new Npgsql.NpgsqlParameter("@id", couponId.ToLong()));
            Assert.Equal(10m, await command.ExecuteScalarAsync());
        }

        await using (var context = CreateContext())
        {
            // Now apply the remaining migrations, including AddShopCouponRules.
            await context.Database.MigrateAsync();

            // The legacy row was backfilled to the Spec's defaults: unlimited
            // (RedemptionLimit null), zero redemptions, version zero — and its
            // original fields are untouched, so it is still usable exactly as
            // before.
            var coupon = await context.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId);
            Assert.Equal("LEGACY10", coupon.Code);
            Assert.Equal(ShopDiscountType.Percentage, coupon.DiscountType);
            Assert.Equal(10m, coupon.DiscountValue);
            Assert.Null(coupon.RedemptionLimit); // unlimited
            Assert.Equal(0, coupon.RedeemedCount);
            Assert.Equal(0, coupon.Version);
            Assert.Equal(0m, coupon.MinimumSubtotal);
            Assert.Null(coupon.MaximumDiscountAmount);
            Assert.True(coupon.IsActive);
        }
    }

    private ShopDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
