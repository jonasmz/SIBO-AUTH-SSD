using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    private readonly Phase1TestResources _resources = new();
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public AuthenticationApiFactory()
    {
        SetEnvironmentVariable("Persistence__ConnectionString", _resources.ConnectionString);
        SetEnvironmentVariable("Jwt__Issuer", "https://auth-api.test");
        SetEnvironmentVariable("Jwt__Audience", "authentication-api-tests");
        SetEnvironmentVariable("Jwt__AccessTokenLifetimeMinutes", "15");
        SetEnvironmentVariable("Jwt__PrivateKeyPath", _resources.PrivateKeyPath);
    }

    public Phase1TestResources Resources => _resources;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
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

            _resources.Dispose();
        }
    }

    private void SetEnvironmentVariable(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
