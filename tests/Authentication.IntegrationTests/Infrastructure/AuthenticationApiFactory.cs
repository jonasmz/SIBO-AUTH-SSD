using Authentication.Application.Features.PasswordRecovery;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
    private readonly TimeSpan? _resetTokenLifespan;
    private readonly bool _useRealEmailSender;
    private readonly string _environment;

    public AuthenticationApiFactory(
        string? connectionStringOverride = null,
        TimeProvider? timeProvider = null,
        int accessTokenLifetimeMinutes = 15,
        Phase1TestResources? sharedResources = null,
        int refreshSessionLifetimeDays = 7,
        string? frontendOrigin = FrontendOrigin,
        IReadOnlyDictionary<string, string>? additionalSettings = null,
        string? dataProtectionKeysPath = null,
        TimeSpan? resetTokenLifespan = null,
        bool useRealEmailSender = false,
        bool liftRateLimits = true,
        string environment = "Testing")
    {
        _environment = environment;
        _resetTokenLifespan = resetTokenLifespan;
        _useRealEmailSender = useRealEmailSender;
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
        SetEnvironmentVariable("DataProtection__KeysPath", dataProtectionKeysPath ?? _resources.DataProtectionKeysPath);
        // Persistent log files go to the resource set's temporary directory; a test can override it through
        // additionalSettings (for example to point at a missing or unwritable directory).
        SetEnvironmentVariable("Logging__File__Directory", _resources.LogsPath);

        // Valid dummy SMTP settings; the default capturing sender never connects to them.
        SetEnvironmentVariable("Smtp__Host", "smtp.test.invalid");
        SetEnvironmentVariable("Smtp__Port", "2525");
        SetEnvironmentVariable("Smtp__Security", "None");
        SetEnvironmentVariable("Smtp__SenderAddress", "no-reply@auth.test");
        SetEnvironmentVariable("Smtp__SenderName", "Authentication API Tests");

        // Request limits are lifted so unrelated scenarios never meet them; a test that supplies a policy's own
        // PermitLimit gets exactly that value, and the remaining policies stay lifted.
        if (liftRateLimits)
        {
            foreach (var policy in new[] { "Login", "Refresh", "ForgotPassword", "ResetPassword", "ForgotPasswordAddress" })
            {
                var key = $"RateLimiting__{policy}__PermitLimit";
                if (additionalSettings?.ContainsKey(key) != true)
                {
                    SetEnvironmentVariable(key, "10000");
                }
            }
        }

        // Extra external settings such as a stricter Identity password policy
        // (for example "Identity__Password__RequiredLength").
        foreach (var (key, value) in additionalSettings ?? new Dictionary<string, string>())
        {
            SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>Counts password verifications performed by the host.</summary>
    public CountingPasswordHasher PasswordHasher { get; } = new();

    /// <summary>Messages the host asked to deliver; empty when the real sender is used.</summary>
    public CapturingEmailSender Emails { get; } = new();

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
        builder.UseEnvironment(_environment);
        builder.ConfigureLogging(logging => logging.AddProvider(_logs));

        builder.ConfigureTestServices(services =>
        {
            if (_timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_timeProvider);
            }

            services.AddSingleton<IStartupFilter, TestConnectionAddressStartupFilter>();
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(PasswordHasher);

            if (!_useRealEmailSender)
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            }

            if (_resetTokenLifespan is { } lifespan)
            {
                services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = lifespan);
            }
        });
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
