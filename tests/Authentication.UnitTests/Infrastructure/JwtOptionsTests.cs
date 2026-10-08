using Authentication.Infrastructure;
using Authentication.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class JwtOptionsTests
{
    [Fact]
    public void HasTheRequiredFifteenMinuteDefaultLifetime()
    {
        var options = new JwtOptions();

        Assert.Equal(15, options.AccessTokenLifetimeMinutes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("30")]
    [InlineData("60")]
    public void AcceptsAClockToleranceFromZeroToSixtySeconds(string seconds)
    {
        using var key = TemporaryKeyFile.Create();

        var exception = Record.Exception(() => Register(key.Path, seconds));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("61")]
    public void RejectsAMissingOrOutOfRangeClockToleranceNamingOnlyTheSetting(string? seconds)
    {
        using var key = TemporaryKeyFile.Create();

        var exception = Assert.Throws<InvalidOperationException>(() => Register(key.Path, seconds));

        Assert.Contains("Jwt:ClockSkewSeconds", exception.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(seconds))
        {
            Assert.DoesNotContain(seconds, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DefaultsRefreshLifetimeToSevenDays()
    {
        using var key = TemporaryKeyFile.Create();
        Assert.Null(Record.Exception(() => Register(key.Path, "30", lifetimeDays: null)));
    }

    [Theory]
    [InlineData("0", "https://frontend.test", "RefreshSession:LifetimeDays")]
    [InlineData("7", "", "Security:FrontendOrigin")]
    [InlineData("7", "not-an-origin", "Security:FrontendOrigin")]
    public void RejectsInvalidRefreshConfigurationWithoutDisclosingValues(string lifetimeDays, string frontendOrigin, string setting)
    {
        using var key = TemporaryKeyFile.Create();
        var exception = Assert.Throws<InvalidOperationException>(() => Register(key.Path, "30", lifetimeDays, frontendOrigin));
        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(frontendOrigin))
        {
            Assert.DoesNotContain(frontendOrigin, exception.Message, StringComparison.Ordinal);
        }
    }

    private static void Register(string privateKeyPath, string? clockSkewSeconds, string? lifetimeDays = "7", string? frontendOrigin = "https://frontend.test")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:ConnectionString"] = "Data Source=unused.db",
            ["Jwt:Issuer"] = "https://auth-api.test",
            ["Jwt:Audience"] = "authentication-api-tests",
            ["Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Jwt:PrivateKeyPath"] = privateKeyPath,
            ["Jwt:ClockSkewSeconds"] = clockSkewSeconds,
            ["RefreshSession:LifetimeDays"] = lifetimeDays,
            ["Security:FrontendOrigin"] = frontendOrigin,
            // Phase 6 settings are required too; valid dummies keep these tests about the settings they name.
            ["Smtp:Host"] = "smtp.test.invalid",
            ["Smtp:Port"] = "2525",
            ["Smtp:Security"] = "None",
            ["Smtp:SenderAddress"] = "no-reply@auth.test",
            ["Smtp:SenderName"] = "Authentication API Tests",
            ["DataProtection:KeysPath"] = Path.GetTempPath(),
            ["Logging:File:Directory"] = Path.GetTempPath()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        new ServiceCollection().AddAuthenticationInfrastructure(configuration);
    }

    private sealed class TemporaryKeyFile : IDisposable
    {
        private TemporaryKeyFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryKeyFile Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.pem");
            File.WriteAllText(path, "placeholder; only readability is checked at registration");

            return new TemporaryKeyFile(path);
        }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
