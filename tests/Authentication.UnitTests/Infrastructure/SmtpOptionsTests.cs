using Authentication.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class SmtpOptionsTests
{
    private static SmtpOptions Valid() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        Security = SmtpSecurity.StartTls,
        Username = "user",
        Password = "secret-value",
        SenderAddress = "no-reply@example.test",
        SenderName = "Authentication API"
    };

    [Fact]
    public void CompleteSettingsAreValidWithOrWithoutCredentials()
    {
        Assert.Null(Valid().FirstInvalidSetting());
        Assert.Null(new SmtpOptions
        {
            Host = "localhost", Port = 25, Security = SmtpSecurity.None,
            SenderAddress = "a@b.test", SenderName = "Name"
        }.FirstInvalidSetting());
    }

    [Theory]
    [InlineData("Host", "Smtp:Host")]
    [InlineData("PortZero", "Smtp:Port")]
    [InlineData("PortHigh", "Smtp:Port")]
    [InlineData("Security", "Smtp:Security")]
    [InlineData("UsernameOnly", "Smtp:Password")]
    [InlineData("PasswordOnly", "Smtp:Username")]
    [InlineData("Sender", "Smtp:SenderAddress")]
    [InlineData("SenderName", "Smtp:SenderName")]
    public void EachInvalidSettingIsNamedWithoutItsValue(string invalid, string expectedSetting)
    {
        var options = invalid switch
        {
            "Host" => new SmtpOptions { Host = " ", Port = 587, Security = SmtpSecurity.StartTls, SenderAddress = "a@b.test", SenderName = "N" },
            "PortZero" => new SmtpOptions { Host = "h", Port = 0, Security = SmtpSecurity.StartTls, SenderAddress = "a@b.test", SenderName = "N" },
            "PortHigh" => new SmtpOptions { Host = "h", Port = 65536, Security = SmtpSecurity.StartTls, SenderAddress = "a@b.test", SenderName = "N" },
            "Security" => new SmtpOptions { Host = "h", Port = 25, Security = null, SenderAddress = "a@b.test", SenderName = "N" },
            "UsernameOnly" => new SmtpOptions { Host = "h", Port = 25, Security = SmtpSecurity.None, Username = "u", SenderAddress = "a@b.test", SenderName = "N" },
            "PasswordOnly" => new SmtpOptions { Host = "h", Port = 25, Security = SmtpSecurity.None, Password = "p", SenderAddress = "a@b.test", SenderName = "N" },
            "Sender" => new SmtpOptions { Host = "h", Port = 25, Security = SmtpSecurity.None, SenderAddress = "not an address", SenderName = "N" },
            _ => new SmtpOptions { Host = "h", Port = 25, Security = SmtpSecurity.None, SenderAddress = "a@b.test", SenderName = "" }
        };

        var setting = options.FirstInvalidSetting();

        Assert.Equal(expectedSetting, setting);
        Assert.DoesNotContain("secret", setting, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("None", SmtpSecurity.None)]
    [InlineData("starttls", SmtpSecurity.StartTls)]
    [InlineData("SslOnConnect", SmtpSecurity.SslOnConnect)]
    [InlineData("Auto", null)]
    [InlineData("StartTlsWhenAvailable", null)]
    [InlineData("1", null)]
    [InlineData("", null)]
    public void OnlyTheThreeExplicitSecurityModesAreAccepted(string value, SmtpSecurity? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Security"] = value, ["Smtp:Port"] = "abc" })
            .Build();

        var options = SmtpOptions.FromConfiguration(configuration);

        Assert.Equal(expected, options.Security);
        Assert.Equal(0, options.Port);
    }
}
