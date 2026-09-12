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

[CollectionDefinition(nameof(TenantDiscoveryIsolatedCollection))]
public sealed class TenantDiscoveryIsolatedCollection : ICollectionFixture<TenantDiscoveryDbFixture>
{
}

[CollectionDefinition(nameof(RolePermissionIsolatedCollection))]
public sealed class RolePermissionIsolatedCollection : ICollectionFixture<RolePermissionDbFixture>
{
}
