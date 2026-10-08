using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AntiEnumerationTests
{
    private const string Password = "Passw0rd!";
    private const string WrongPassword = "Wr0ng-Guess!";

    [Fact]
    public async Task LoginIsIndistinguishableAndDoesTheSameVerificationWorkWhateverTheAccountState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", Password, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "locked@example.test", Password, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "disabled@example.test", Password, enabled: false, cancellationToken: cancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            _ = await LoginAsync(client, "locked@example.test", WrongPassword, cancellationToken);
        }

        var cases = new (string Name, string Email, string Pass)[]
        {
            ("unknown account", "nobody@example.test", WrongPassword),
            ("wrong password", "member@example.test", WrongPassword),
            ("locked account", "locked@example.test", Password),
            ("disabled account", "disabled@example.test", Password)
        };

        string? reference = null;
        foreach (var (name, email, pass) in cases)
        {
            var before = factory.PasswordHasher.Verifications;
            using var response = await LoginAsync(client, email, pass, cancellationToken);
            var verifications = factory.PasswordHasher.Verifications - before;

            // Same status, message, and structure, and the same single password verification: the unknown
            // account does not end early. Work is measured by calls, never by duration.
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {(int)response.StatusCode}");
            var shape = Shape(await response.Content.ReadAsStringAsync(cancellationToken));
            reference ??= shape;
            Assert.True(shape == reference, $"{name}: the response differs from the others");
            Assert.True(verifications == 1, $"{name}: {verifications} verifications");
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        }

        // The real cause is still known to operators, through events that carry no email or password.
        var failures = factory.CapturedLogs.Where(log => log.StartsWith("LoginFailed", StringComparison.Ordinal)).ToList();
        foreach (var reason in new[] { "UnknownAccount", "WrongPassword", "LockedOut", "Disabled" })
        {
            Assert.Contains(failures, log => log.StartsWith($"LoginFailed: reason {reason}", StringComparison.Ordinal));
        }

        Assert.All(failures, log =>
        {
            Assert.DoesNotContain("example.test", log, StringComparison.Ordinal);
            Assert.DoesNotContain(WrongPassword, log, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, log, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RecoveryAndResetRevealNothingAboutWhichAccountsExistOrTheirState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", Password, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "disabled@example.test", Password, enabled: false, cancellationToken: cancellationToken);

        var addresses = new[] { "member@example.test", "nobody@example.test", "disabled@example.test" };

        // Forgot-password: one identical answer for an existing, an unknown, and a disabled account.
        var forgot = new List<string>();
        foreach (var address in addresses)
        {
            forgot.Add(await PasswordRecoveryRequestTests.ForgotAsync(client, address, cancellationToken));
        }

        Assert.Equal("204||", forgot[0]);
        Assert.All(forgot, answer => Assert.Equal(forgot[0], answer));

        // Reset-password: unknown account, disabled account, and a bad token are the same single 401.
        var resets = new List<string>();
        foreach (var address in addresses)
        {
            using var response = await PasswordResetTests.ResetAsync(client, address, "not-a-token", "N3w-Secret!", cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            resets.Add(Shape(await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        Assert.All(resets, shape => Assert.Equal(resets[0], shape));
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);

    /// <summary>The comparable structure of a problem response: every member except the per-request trace id.</summary>
    private static string Shape(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);

        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => $"{property.Name}={property.Value}"));
    }
}
