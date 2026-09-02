using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Infrastructure;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

public sealed class IamPersistenceIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("tenantforge_iam_tests")
        .WithUsername("tenantforge")
        .WithPassword("tenantforge")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Migration_CreatesCleanIamAccountTable()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select column_name, is_nullable
            from information_schema.columns
            where table_schema = 'public' and table_name = 'iam_accounts'
            order by ordinal_position;
            """;

        var columns = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Equal("NO", columns["id"]);
        Assert.Equal("NO", columns["email"]);
        Assert.Equal("NO", columns["normalized_email"]);
        Assert.Equal("NO", columns["display_name"]);
        Assert.Equal("NO", columns["password_hash"]);
        Assert.Equal("NO", columns["is_platform_admin"]);
        Assert.Equal("NO", columns["status"]);
        Assert.Equal("NO", columns["created_at_utc"]);
        Assert.Equal("NO", columns["updated_at_utc"]);
    }

    [Fact]
    public async Task NormalizedEmail_IsUniqueInPostgreSql()
    {
        await using var db = CreateContext();
        var now = DateTimeOffset.UtcNow;

        db.Accounts.Add(Account.CreatePlatformAdmin("admin@tenantforge.local", "Platform Admin", "hash-one", now));
        db.Accounts.Add(Account.CreatePlatformAdmin(" ADMIN@TENANTFORGE.LOCAL ", "Duplicate Admin", "hash-two", now));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RequiredColumns_AreEnforcedByPostgreSql()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into iam_accounts
                (id, email, normalized_email, display_name, password_hash, is_platform_admin, status, created_at_utc, updated_at_utc)
            values
                (@id, @email, @normalizedEmail, @displayName, @passwordHash, @isPlatformAdmin, @status, @createdAtUtc, @updatedAtUtc);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("email", DBNull.Value);
        command.Parameters.AddWithValue("normalizedEmail", "MISSING@TENANTFORGE.LOCAL");
        command.Parameters.AddWithValue("displayName", "Missing Email");
        command.Parameters.AddWithValue("passwordHash", "hash-only");
        command.Parameters.AddWithValue("isPlatformAdmin", true);
        command.Parameters.AddWithValue("status", "Active");
        command.Parameters.AddWithValue("createdAtUtc", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("updatedAtUtc", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public void AccountModel_StoresPasswordHashButNoPlaintextPasswordField()
    {
        var propertyNames = typeof(Account).GetProperties().Select(property => property.Name).ToArray();
        var fieldNames = typeof(Account).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();

        Assert.Contains(nameof(Account.PasswordHash), propertyNames);
        Assert.DoesNotContain("Password", propertyNames);
        Assert.DoesNotContain("PlaintextPassword", propertyNames);
        Assert.DoesNotContain("Password", fieldNames);
        Assert.DoesNotContain("PlaintextPassword", fieldNames);
    }

    private IamDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IamDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new IamDbContext(options);
    }
}
