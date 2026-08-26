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

public enum DevelopmentLoginMode
{
    Absent,
    Complete,
    Partial
}

public sealed class ApiFactory(string environment, DevelopmentLoginMode developmentLoginMode) : WebApplicationFactory<Program>, IDisposable
{
    public const string Email = "admin@tenantforge.local";
    public const string Password = "local-development-password";
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
            ["AllowedOrigins:0"] = "http://localhost:5173"
        };

        switch (developmentLoginMode)
        {
            case DevelopmentLoginMode.Complete:
                values["IAM:DevelopmentLogin:Email"] = Email;
                values["IAM:DevelopmentLogin:Password"] = Password;
                values["IAM:DevelopmentLogin:SigningKey"] = SigningKey;
                break;
            case DevelopmentLoginMode.Partial:
                values["IAM:DevelopmentLogin:Email"] = Email;
                values["IAM:DevelopmentLogin:Password"] = Password;
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
