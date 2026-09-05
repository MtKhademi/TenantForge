using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TenantForge.Modules.Iam.Domain;
using TenantForge.Modules.Iam.Features.Login;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// Exercises <see cref="PlatformAdminSeeder"/> directly against a real,
/// isolated PostgreSQL database (its own container per test class, not the
/// shared API fixture) so seeding behavior — including a deliberately induced
/// race — can be observed without interference from other tests' seeded rows.
/// </summary>
public sealed class PlatformAdminSeederTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres =
        new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tenantforge_seed_tests")
            .WithUsername("tenantforge")
            .WithPassword("tenantforge")
            .Build();

    private static readonly SeedAdminOptions Options = new()
    {
        Email = "admin@tenantforge.local",
        Password = "local-development-password",
        DisplayName = "Platform Administrator"
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task FirstSeed_CreatesExactlyOnePlatformAdministrator()
    {
        await using var db = CreateContext();
        var seeder = new PlatformAdminSeeder(db, new PasswordHasher<Account>());

        var created = await seeder.SeedAsync(Options, DateTimeOffset.UtcNow);

        Assert.True(created);
        Assert.Equal(1, await db.Accounts.CountAsync());

        var account = await db.Accounts.SingleAsync();
        Assert.Equal(Account.NormalizeEmail(Options.Email!), account.NormalizedEmail);
        Assert.True(account.IsPlatformAdmin);
        Assert.Equal(AccountStatus.Active, account.Status);
        // The stored value is a hash, never the configured plaintext password.
        Assert.NotEqual(Options.Password, account.PasswordHash);
    }

    [Fact]
    public async Task RepeatedSeed_DoesNotDuplicateTheAdministrator()
    {
        await using (var db = CreateContext())
        {
            var seeder = new PlatformAdminSeeder(db, new PasswordHasher<Account>());
            Assert.True(await seeder.SeedAsync(Options, DateTimeOffset.UtcNow));
        }

        // Simulates a second process startup against the same database.
        await using (var db = CreateContext())
        {
            var seeder = new PlatformAdminSeeder(db, new PasswordHasher<Account>());
            var createdOnSecondRun = await seeder.SeedAsync(Options, DateTimeOffset.UtcNow);

            Assert.False(createdOnSecondRun);
            Assert.Equal(1, await db.Accounts.CountAsync());
        }
    }

    [Fact]
    public async Task ConcurrentSeed_RacingProcesses_StillCreateExactlyOneAdministrator()
    {
        // Two independent DbContexts (simulating two API processes racing to
        // seed on startup against the same clean database) both attempt to seed
        // at the same time.
        await using var dbOne = CreateContext();
        await using var dbTwo = CreateContext();
        var seederOne = new PlatformAdminSeeder(dbOne, new PasswordHasher<Account>());
        var seederTwo = new PlatformAdminSeeder(dbTwo, new PasswordHasher<Account>());

        var resultOne = seederOne.SeedAsync(Options, DateTimeOffset.UtcNow);
        var resultTwo = seederTwo.SeedAsync(Options, DateTimeOffset.UtcNow);
        var results = await Task.WhenAll(resultOne, resultTwo);

        // Exactly one of the two racing seeders must have created the row; the
        // unique index on normalized_email guarantees the other loses the race
        // rather than producing a duplicate.
        Assert.Single(results, created => created);

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Accounts.CountAsync());
    }

    private TenantForge.Modules.Iam.Infrastructure.IamDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TenantForge.Modules.Iam.Infrastructure.IamDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}

/// <summary>
/// Login behavior for a disabled account, exercised via a dedicated,
/// isolated database so a directly-manipulated account status does not
/// interfere with the shared API-test fixture's seeded admin.
/// </summary>
public sealed class DisabledAccountLoginTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres =
        new Testcontainers.PostgreSql.PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("tenantforge_disabled_tests")
            .WithUsername("tenantforge")
            .WithPassword("tenantforge")
            .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task DisabledAccount_LoginReturns401_IndistinguishableFromWrongPassword()
    {
        const string email = "disabled-admin@tenantforge.local";
        const string password = "local-development-password";

        var hasher = new PasswordHasher<Account>();
        await using (var db = CreateContext())
        {
            var account = Account.CreatePlatformAdmin(email, "Disabled Admin", "placeholder", DateTimeOffset.UtcNow);
            var hash = hasher.HashPassword(account, password);
            SetPasswordHash(account, hash);
            Disable(account);
            db.Accounts.Add(account);
            await db.SaveChangesAsync();
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-iam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            using var factory = new DisabledAccountApiFactory(_postgres.GetConnectionString(), contentRoot);
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

            // A correct password for a disabled account must fail exactly like a
            // wrong password: a generic 401, never a distinguishing signal.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Invalid credentials", body);
        }
        finally
        {
            try
            {
                Directory.Delete(contentRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private TenantForge.Modules.Iam.Infrastructure.IamDbContext CreateContext() => new(
        new DbContextOptionsBuilder<TenantForge.Modules.Iam.Infrastructure.IamDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    // Account's setters are private; these small reflection helpers let the
    // test arrange a disabled row without adding a public "disable" API to the
    // domain model purely for test convenience (disabling accounts is out of
    // scope for B005).
    private static void SetPasswordHash(Account account, string hash) =>
        typeof(Account).GetProperty(nameof(Account.PasswordHash))!.SetValue(account, hash);

    private static void Disable(Account account) =>
        typeof(Account).GetProperty(nameof(Account.Status))!.SetValue(account, AccountStatus.Disabled);
}

internal sealed class DisabledAccountApiFactory(string connectionString, string contentRoot)
    : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseContentRoot(contentRoot);
        builder.UseEnvironment("Development");

        var values = new Dictionary<string, string?>
        {
            ["IAM:IamDb"] = connectionString,
            ["IAM:Auth:SigningKey"] = "dev-only-tenantforge-signing-key-do-not-use-32b"
            // No IAM:SeedAdmin: the account under test was inserted directly,
            // so seeding must stay disabled to avoid seeding an unrelated admin.
        };

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));
    }
}
