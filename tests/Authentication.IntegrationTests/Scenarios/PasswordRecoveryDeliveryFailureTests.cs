using System.Net;
using System.Net.Sockets;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordRecoveryDeliveryFailureTests
{
    private const string SmtpPassword = "smtp-secret-pw";

    [Fact]
    public async Task AFailedDeliveryIsLoggedSafelyAndInvisibleToTheCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A port that was just bound and released: the connection is refused, deterministically.
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        using var factory = new AuthenticationApiFactory(
            useRealEmailSender: true,
            additionalSettings: new Dictionary<string, string>
            {
                ["Smtp__Host"] = "127.0.0.1",
                ["Smtp__Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Smtp__Security"] = "None",
                ["Smtp__Username"] = "smtp-user",
                ["Smtp__Password"] = SmtpPassword
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminToken = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);

        var existing = await PasswordRecoveryRequestTests.ForgotAsync(client, AdminTestSupport.AdministratorEmail, cancellationToken);
        var unknown = await PasswordRecoveryRequestTests.ForgotAsync(client, "nobody@example.test", cancellationToken);

        Assert.Equal("204||", existing);
        Assert.Equal(existing, unknown);

        var failure = Assert.Single(factory.CapturedLogs, log => log.StartsWith("EmailDeliveryFailed", StringComparison.Ordinal));
        Assert.Contains("127.0.0.1", failure, StringComparison.Ordinal);
        Assert.Contains($":{port}", failure, StringComparison.Ordinal);
        Assert.Matches(@"at \d{4}-\d{2}-\d{2}T[\d:.]+\+00:00", failure);
        Assert.Matches(@"trace [0-9a-f]{32}", failure);

        // Nothing sensitive: not the recipient, subject, token line, SMTP credentials, or an access token.
        foreach (var secret in new[] { AdminTestSupport.AdministratorEmail, "Password reset instructions", "Token:", SmtpPassword, "smtp-user", adminToken })
        {
            Assert.DoesNotContain(factory.CapturedLogs, log => log.Contains(secret, StringComparison.Ordinal));
        }
    }
}
