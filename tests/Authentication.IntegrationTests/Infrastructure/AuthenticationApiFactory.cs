using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    public const string FrontendOrigin = "https://frontend.test";
    private readonly Phase1TestResources _resources;
    private readonly bool _ownsResources;
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    private readonly TimeProvider? _timeProvider;
    private readonly CapturingLoggerProvider _logs = new();

    public AuthenticationApiFactory(
        string? connectionStringOverride = null,
        TimeProvider? timeProvider = null,
        int accessTokenLifetimeMinutes = 15,
        Phase1TestResources? sharedResources = null,
        int refreshSessionLifetimeDays = 7,
        string? frontendOrigin = FrontendOrigin,
        IReadOnlyDictionary<string, string>? additionalSettings = null)
    {
        _ownsResources = sharedResources is null;
        _resources = sharedResources ?? new Phase1TestResources();
        _timeProvider = timeProvider;
        SetEnvironmentVariable("Persistence__ConnectionString", connectionStringOverride ?? _resources.ConnectionString);
        SetEnvironmentVariable("Jwt__Issuer", "https://auth-api.test");
        SetEnvironmentVariable("Jwt__Audience", "authentication-api-tests");
        SetEnvironmentVariable("Jwt__AccessTokenLifetimeMinutes", accessTokenLifetimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetEnvironmentVariable("Jwt__PrivateKeyPath", _resources.PrivateKeyPath);
        SetEnvironmentVariable("Jwt__ClockSkewSeconds", "30");
        SetEnvironmentVariable("RefreshSession__LifetimeDays", refreshSessionLifetimeDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetEnvironmentVariable("Security__FrontendOrigin", frontendOrigin ?? string.Empty);

        // Extra external settings such as a stricter Identity password policy
        // (for example "Identity__Password__RequiredLength").
        foreach (var (key, value) in additionalSettings ?? new Dictionary<string, string>())
        {
            SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>Formatted log messages emitted by the host, for secret-scanning assertions.</summary>
    public IReadOnlyList<string> CapturedLogs => _logs.Snapshot();

    public Phase1TestResources Resources => _resources;

    public static HttpRequestMessage CreateBrowserRequest(HttpMethod method, string path, string? refreshCredential = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", FrontendOrigin);
        if (refreshCredential is not null)
        {
            request.Headers.Add("Cookie", $"auth_refresh={refreshCredential}");
        }

        return request;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(_logs));

        if (_timeProvider is not null)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            foreach (var (key, value) in _originalEnvironment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (_ownsResources)
            {
                _resources.Dispose();
            }
        }
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Snapshot()
    {
        lock (_messages)
        {
            return [.. _messages];
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}
