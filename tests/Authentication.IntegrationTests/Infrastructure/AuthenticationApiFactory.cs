using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    private readonly Phase1TestResources _resources = new();
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    private readonly TimeProvider? _timeProvider;

    public AuthenticationApiFactory(
        string? connectionStringOverride = null,
        TimeProvider? timeProvider = null,
        int accessTokenLifetimeMinutes = 15)
    {
        _timeProvider = timeProvider;
        SetEnvironmentVariable("Persistence__ConnectionString", connectionStringOverride ?? _resources.ConnectionString);
        SetEnvironmentVariable("Jwt__Issuer", "https://auth-api.test");
        SetEnvironmentVariable("Jwt__Audience", "authentication-api-tests");
        SetEnvironmentVariable("Jwt__AccessTokenLifetimeMinutes", accessTokenLifetimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetEnvironmentVariable("Jwt__PrivateKeyPath", _resources.PrivateKeyPath);
    }

    public Phase1TestResources Resources => _resources;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
            _resources.Dispose();
        }
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
