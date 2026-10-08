using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ReferenceConsumer.Api;

namespace Authentication.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the reference consumer as <c>api-a</c> or <c>api-b</c>. Configuration is applied per host
/// (not through process-wide environment variables), so several hosts can be alive at once.
/// </summary>
public sealed class ReferenceConsumerFactory : WebApplicationFactory<ReferenceConsumerEntryPoint>
{
    private readonly Dictionary<string, string> _settings;
    private readonly string _rootPath;

    public ReferenceConsumerFactory(
        string serviceName,
        string publicKeyPem,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "reference-consumer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
        PublicKeyPath = Path.Combine(_rootPath, "jwt-public.pem");
        File.WriteAllText(PublicKeyPath, publicKeyPem);

        _settings = new Dictionary<string, string>
        {
            ["Service:Name"] = serviceName,
            ["Jwt:Issuer"] = TestTokenMinter.DefaultIssuer,
            ["Jwt:Audience"] = TestTokenMinter.DefaultAudience,
            ["Jwt:PublicKeyPath"] = PublicKeyPath,
            ["Jwt:ClockSkewSeconds"] = "30"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                _settings[key] = value;
            }
        }
    }

    public string PublicKeyPath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
