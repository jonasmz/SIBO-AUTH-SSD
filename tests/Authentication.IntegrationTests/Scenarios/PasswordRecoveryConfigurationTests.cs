using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordRecoveryConfigurationTests
{
    private const string KeysPathSetting = "DataProtection:KeysPath";

    [Theory]
    [InlineData("Smtp__Host", "", "Smtp:Host")]
    [InlineData("Smtp__Port", "not-a-port", "Smtp:Port")]
    [InlineData("Smtp__Port", "70000", "Smtp:Port")]
    [InlineData("Smtp__Security", "Auto", "Smtp:Security")]
    [InlineData("Smtp__Username", "only-a-username", "Smtp:Password")]
    [InlineData("Smtp__Password", "only-a-secret-password", "Smtp:Username")]
    [InlineData("Smtp__SenderAddress", "not an address", "Smtp:SenderAddress")]
    [InlineData("Smtp__SenderName", "", "Smtp:SenderName")]
    public void AnInvalidSmtpSettingStopsStartupNamingOnlyTheSetting(string key, string value, string expectedSetting)
    {
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string> { [key] = value });

        var message = StartupFailureMessage(factory);

        Assert.Contains(expectedSetting, message, StringComparison.Ordinal);
        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingOrUnwritableKeyRingDirectoryStopsStartupWithoutEchoingThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "secret-keys-dir");
        using var absent = new AuthenticationApiFactory(dataProtectionKeysPath: missing);
        var absentMessage = StartupFailureMessage(absent);

        Assert.Contains(KeysPathSetting, absentMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-keys-dir", absentMessage, StringComparison.Ordinal);

        // A regular file where a directory is expected cannot hold keys either.
        var file = Path.Combine(Path.GetTempPath(), $"keys-file-{Guid.NewGuid():N}");
        File.WriteAllText(file, string.Empty);
        try
        {
            using var notADirectory = new AuthenticationApiFactory(dataProtectionKeysPath: file);
            var fileMessage = StartupFailureMessage(notADirectory);

            Assert.Contains(KeysPathSetting, fileMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(file, fileMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string StartupFailureMessage(AuthenticationApiFactory factory)
    {
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var messages = new List<string>();
        for (var current = failure; current is not null; current = current.InnerException!)
        {
            messages.Add(current.Message);
            if (current.InnerException is null)
            {
                break;
            }
        }

        return string.Join(" | ", messages);
    }
}
