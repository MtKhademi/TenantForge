using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TenantForge.Api.IntegrationTests;

public sealed class TestLogCollector
{
    private readonly List<string> _entries = [];
    public IReadOnlyList<string> Entries => _entries;
    public void Add(string entry) => _entries.Add(entry);
    public void Clear() => _entries.Clear();
}

/// <summary>
/// Which IAM configuration the test host gets: the seeded-admin + signing-key
/// sections (Complete), a partially-filled seed section (Partial, used to prove
/// startup validation), or neither (Absent, used to prove fail-closed behavior).
/// </summary>
public enum IamSeedMode
{
    Absent,
    Complete,
    Partial,
    NoSigningKey
}

public sealed class ApiFactory(string environment, IamSeedMode seedMode, IamDbFixture db)
    : WebApplicationFactory<Program>, IDisposable
{
    public const string Email = "admin@tenantforge.local";
    public const string Password = "local-development-password";
    public const string DisplayName = "Platform Administrator";
    public const string SigningKey = "dev-only-tenantforge-signing-key-do-not-use-32b";

    public TestLogCollector LogCollector { get; } = new();

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "tenantforge-iam-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRoot);
        builder.UseContentRoot(_contentRoot);
        builder.UseEnvironment(environment);

        var values = new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Debug",
            ["AllowedOrigins:0"] = "http://localhost:5173",
            // Real PostgreSQL from the shared fixture: login is now
            // database-backed, so the test host must point at a real, migrated
            // database (an in-memory or absent database is not representative).
            ["IAM:IamDb"] = db.ConnectionString
        };

        switch (seedMode)
        {
            case IamSeedMode.Complete:
                values["IAM:Auth:SigningKey"] = SigningKey;
                values["IAM:SeedAdmin:Email"] = Email;
                values["IAM:SeedAdmin:Password"] = Password;
                values["IAM:SeedAdmin:DisplayName"] = DisplayName;
                break;
            case IamSeedMode.Partial:
                values["IAM:SeedAdmin:Email"] = Email;
                values["IAM:SeedAdmin:Password"] = Password;
                break;
            case IamSeedMode.NoSigningKey:
                // Seeding is configured, but no signing key: startup succeeds,
                // but the login endpoint must fail closed because it cannot mint
                // a verifiable token.
                values["IAM:SeedAdmin:Email"] = Email;
                values["IAM:SeedAdmin:Password"] = Password;
                values["IAM:SeedAdmin:DisplayName"] = DisplayName;
                break;
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(values));

        builder.ConfigureLogging((_, logging) =>
            logging.AddProvider(new CollectorLoggerProvider(LogCollector)));
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

internal sealed class CollectorLoggerProvider(TestLogCollector collector) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CollectorLogger(collector);

    public void Dispose()
    {
    }
}

internal sealed class CollectorLogger(TestLogCollector collector) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        collector.Add(formatter(state, exception));
    }
}
