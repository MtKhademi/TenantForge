using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TenantForge.Modules.Iam.Infrastructure;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Shared, migrated PostgreSQL for the API-level tests. One container is started
/// once per collection; the app's Program applies migrations and seeds the
/// platform administrator on host startup, so this fixture only needs to bring
/// the database up. The seeded admin is created by whichever host starts first;
/// because seeding is idempotent, later hosts in the collection see it already
/// present and do not duplicate it.
/// </summary>
public sealed class IamDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenantforge_iam_tests")
        .WithUsername("tenantforge")
        .WithPassword("tenantforge")
        .Build();

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

[CollectionDefinition(nameof(IamApiTestCollection))]
public sealed class IamApiTestCollection : ICollectionFixture<IamDbFixture>
{
}
