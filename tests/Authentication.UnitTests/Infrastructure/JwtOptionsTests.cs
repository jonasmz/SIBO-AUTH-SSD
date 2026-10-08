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

    private static void Register(string privateKeyPath, string? clockSkewSeconds)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:ConnectionString"] = "Data Source=unused.db",
            ["Jwt:Issuer"] = "https://auth-api.test",
            ["Jwt:Audience"] = "authentication-api-tests",
            ["Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Jwt:PrivateKeyPath"] = privateKeyPath,
            ["Jwt:ClockSkewSeconds"] = clockSkewSeconds
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
