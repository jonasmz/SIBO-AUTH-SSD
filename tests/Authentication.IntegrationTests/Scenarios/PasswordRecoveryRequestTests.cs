using System.Net;
using System.Text;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordRecoveryRequestTests
{
    internal const string ForgotPath = "/api/auth/forgot-password";
    private const string Email = "member@example.test";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task EveryWellFormedRequestGetsTheSameResponseAndOnlyAnEnabledAccountIsEmailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, Password, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "disabled@example.test", Password, enabled: false, cancellationToken: cancellationToken);
        var cookie = await AdministrativeSessionRevocationTests.LoginCookieAsync(client, Email, Password, cancellationToken);

        var enabled = await ForgotAsync(client, Email, cancellationToken);
        var unknown = await ForgotAsync(client, "nobody@example.test", cancellationToken);
        var disabled = await ForgotAsync(client, "disabled@example.test", cancellationToken);

        Assert.Equal("204||", enabled);
        Assert.Equal(enabled, unknown);
        Assert.Equal(enabled, disabled);

        // Exactly one message, for the enabled account only, carrying the token in the body.
        var message = Assert.Single(factory.Emails.Messages);
        Assert.Equal(Email, message.ToAddress);
        var token = CapturingEmailSender.TokenOf(message);
        Assert.NotEmpty(token);

        // The same address in a different letter case is the same account.
        Assert.Equal(enabled, await ForgotAsync(client, "MEMBER@Example.TEST", cancellationToken));
        Assert.Equal(2, factory.Emails.Messages.Count);

        // A recovery request alone revokes no session.
        using var refresh = await AdministrativeSessionRevocationTests.RefreshAsync(client, cookie, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Unusable requests are rejected independently of account existence and never reach the sender.
        var before = factory.Emails.Messages.Count;
        foreach (var (content, mediaType) in new (string, string)[]
        {
            ("email=member@example.test", "text/plain"),
            ("{ not json", "application/json"),
            ("{}", "application/json"),
            ("""{"email":"   "}""", "application/json"),
            ("""{"email":"not-an-email"}""", "application/json")
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ForgotPath)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            };
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The request is invalid.", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        }

        Assert.Equal(before, factory.Emails.Messages.Count);

        // The token never appears in a log.
        Assert.DoesNotContain(factory.CapturedLogs, log => log.Contains(token, StringComparison.Ordinal));
        Assert.Contains(factory.CapturedLogs, log => log.StartsWith("PasswordResetRequested", StringComparison.Ordinal));
    }

    /// <summary>The externally visible part of a response: status, headers other than Date, and the body.</summary>
    internal static async Task<string> ForgotAsync(HttpClient client, string email, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ForgotPath)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { email }), Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request, cancellationToken);
        var headers = string.Join(
            ";",
            response.Headers.Concat(response.Content.Headers)
                .Where(header => !string.Equals(header.Key, "Date", StringComparison.OrdinalIgnoreCase))
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => $"{header.Key}={string.Join(",", header.Value)}"));

        return $"{(int)response.StatusCode}|{headers}|{await response.Content.ReadAsStringAsync(cancellationToken)}";
    }
}
