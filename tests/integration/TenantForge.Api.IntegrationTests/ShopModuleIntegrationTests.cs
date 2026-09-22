using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Protects the Shop module's persistence skeleton and two-method composition
/// seam introduced by B025. These tests prove the startup contract: a missing
/// Shop:ShopDb fails closed, a set Shop:ShopDb applies exactly the Shop-owned
/// tables plus a Shop-owned migration history table, and a restart against the
/// same database is a no-op. ExpectedShopTables is the complete, exact set of
/// Shop tables: every task that adds a Shop table must add it here in the
/// same change (B025: the six catalog tables; B028: the two cart tables;
/// B029: shop_shipping_rates and shop_coupons; B031: shop_orders and
/// shop_order_items; B032: shop_payment_attempts; B036: shop_product_images;
/// B039: shop_profiles).
///
/// Shop:ShopDb deliberately points at the SAME physical database as IAM:IamDb
/// (a named B025 decision — later Shop admin tasks read IAM membership with
/// raw SQL, which only works when both live in one database), so these tests
/// also prove the two modules' migrations coexist in one database with their
/// own, separate migration-history tables.
/// </summary>
[Collection(nameof(ShopIsolatedCollection))]
public sealed class ShopModuleIntegrationTests(ShopDbFixture db)
{
    private static readonly string[] ExpectedShopTables =
    [
        "shop_categories",
        "shop_products",
        "shop_product_variants",
        "shop_product_images",
        "shop_size_guide_cells",
        "shop_size_guide_columns",
        "shop_size_guide_rows",
        "shop_shipping_rates",
        "shop_coupons",
        "shop_carts",
        "shop_cart_items",
        "shop_orders",
        "shop_order_items",
        "shop_payment_attempts",
        "shop_profiles"
    ];

    [Fact]
    public void MissingShopConnection_FailsClosedAtStartup()
    {
        using var factory = new ShopFailClosedApiFactory(db.ConnectionString);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("Shop:ShopDb", exception.Message, StringComparison.Ordinal);
        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Shop:CartReservationMinutes", "4", "5 through 1440")]
    [InlineData("Shop:CartCleanupIntervalSeconds", "29", "30 through 3600")]
    public void InvalidCartLeaseConfiguration_FailsClosedAtStartup(string key, string value, string expectedMessage)
    {
        using var factory = new ShopInvalidCartLeaseApiFactory(db.ConnectionString, key, value);

        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshStartup_CreatesExactlyTheSixCatalogTablesAndOwnHistoryTable()
    {
        using var factory = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db);
        using var client = factory.CreateClient();

        // The host ran UseIamModuleAsync then UseShopModuleAsync to completion,
        // which is itself proof both migrations applied in one physical database.
        var tables = await QueryShopTablesAsync(db.ConnectionString);

        // Compare as a set (ordinal-sorted): PostgreSQL's C collation orders
        // "shop_product_variants" before "shop_products", so an unordered
        // equality check would fail on order alone.
        Assert.Equal(ExpectedShopTables.OrderBy(name => name, StringComparer.Ordinal),
            tables.OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(await MigrationHistoryHasAppliedInitialCatalogAsync(db.ConnectionString));
    }

    [Fact]
    public async Task RepeatStartup_AgainstTheSameDatabase_IsANoOp()
    {
        using (var firstHost = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db))
        {
            using var firstClient = firstHost.CreateClient();
            var firstHealth = await firstClient.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, firstHealth.StatusCode);
        }

        // A second, independent host activates against the same database,
        // simulating a process restart. UseShopModuleAsync must run MigrateAsync
        // again without error and without creating a second history row.
        using var secondHost = new ApiFactory(environment: "Development", seedMode: IamSeedMode.Complete, db);
        using var secondClient = secondHost.CreateClient();

        var health = await secondClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        Assert.Equal(ExpectedShopTables.OrderBy(name => name, StringComparer.Ordinal),
            (await QueryShopTablesAsync(db.ConnectionString)).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(1, await CountInitialCatalogHistoryRowsAsync(db.ConnectionString));
    }

    private static async Task<string[]> QueryShopTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // In this raw string literal the SQL is exactly as written: the single
        // backslash escapes the underscore so the pattern matches a literal
        // "shop_" prefix (without it the underscore would match any one char).
        command.CommandText = """
            select table_name
            from information_schema.tables
            where table_schema = 'public' and table_name like 'shop\_%'
            order by table_name;
            """;

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables.ToArray();
    }

    private static async Task<bool> MigrationHistoryHasAppliedInitialCatalogAsync(string connectionString)
        => await CountInitialCatalogHistoryRowsAsync(connectionString) >= 1;

    private static async Task<int> CountInitialCatalogHistoryRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // The table is "__ShopMigrationsHistory" (ShopConfig), deliberately
        // distinct from IAM's "__EFMigrationsHistory" so the two modules'
        // migration histories coexist in the same physical database. Npgsql
        // preserves identifier case (the tables are lowercase only because the
        // maps name them lowercase), and EF Core names the history columns
        // "MigrationId"/"ProductVersion", so both are quoted as-is. EF stores
        // the full migration id (timestamp + name), so match on the name
        // suffix rather than an exact timestamp that would be brittle.
        command.CommandText = """
            select count(*)
            from "__ShopMigrationsHistory"
            where "MigrationId" like '%InitialShopCatalog';
            """;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}

/// <summary>
/// A Development host that configures IAM fully but deliberately omits
/// Shop:ShopDb, used only to prove the Shop activation seam fails closed at
/// startup rather than serving traffic with an unconfigured database.
/// </summary>
internal sealed class ShopInvalidCartLeaseApiFactory(string connectionString, string invalidKey, string invalidValue)
    : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-shop-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Development");

        var values = new Dictionary<string, string?>
        {
            ["IAM:IamDb"] = connectionString,
            ["IAM:Auth:SigningKey"] = "dev-only-tenantforge-signing-key-do-not-use-32b",
            ["Shop:ShopDb"] = connectionString,
            ["Shop:MediaRoot"] = Path.Combine(_contentRoot, "shop-media"),
            ["Shop:CartCleanupIntervalSeconds"] = "3600",
            [invalidKey] = invalidValue
        };

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));
    }

    public new void Dispose()
    {
        base.Dispose();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class ShopFailClosedApiFactory(string connectionString)
    : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-shop-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment("Development");

        var values = new Dictionary<string, string?>
        {
            ["IAM:IamDb"] = connectionString,
            ["IAM:Auth:SigningKey"] = "dev-only-tenantforge-signing-key-do-not-use-32b"
            // No Shop:ShopDb on purpose: the whole point is that the Shop
            // activation seam must refuse to start without it.
        };

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));
    }

    public new void Dispose()
    {
        base.Dispose();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
