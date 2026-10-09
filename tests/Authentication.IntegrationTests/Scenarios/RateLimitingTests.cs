using System.Net;
using System.Net.Http.Json;
using System.Text;
using Authentication.Infrastructure.Identity;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RateLimitingTests
{
    private const string AdminAddress = "198.51.100.1";

    // The documented default windows; the tests only lower permit limits, so these stay in force.
    private const int LoginWindowSeconds = 60;
    private const int RefreshWindowSeconds = 60;
    private const int ForgotPasswordWindowSeconds = 900;
    private const int ResetPasswordWindowSeconds = 900;
    private const int ForgotPasswordAddressWindowSeconds = 3600;

    [Fact]
    public async Task AnIPv4AddressAndItsIPv4MappedIPv6FormShareOneAllowance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Login__PermitLimit"] = "2"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // One client reaching the service over IPv4 and over a dual-stack socket is one origin.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "192.0.2.90", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "::ffff:192.0.2.90", cancellationToken)).StatusCode);
        using var limited = await LoginAsync(client, "::ffff:192.0.2.90", cancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfterWithin(limited, LoginWindowSeconds);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "192.0.2.90", cancellationToken)).StatusCode);

        // The event records the folded IPv4 form.
        Assert.All(
            factory.CapturedLogs.Where(log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal)),
            log =>
            {
                Assert.Contains("client 192.0.2.90 ", log, StringComparison.Ordinal);
                Assert.DoesNotContain("::ffff:", log, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task EachOfTheFourPoliciesLimitsItselfIndependentlyPerAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Login__PermitLimit"] = "3",
            ["RateLimiting__Refresh__PermitLimit"] = "2",
            ["RateLimiting__ForgotPassword__PermitLimit"] = "2",
            ["RateLimiting__ResetPassword__PermitLimit"] = "2"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        const string address = "192.0.2.50";

        // Within the limit every endpoint answers exactly as before.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, address, cancellationToken)).StatusCode);
        }

        // The login policy is exhausted for this address; a 429 is a problem+json without account data.
        using (var limited = await LoginAsync(client, address, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
            AssertRetryAfterWithin(limited, LoginWindowSeconds);
            var body = await limited.Content.ReadAsStringAsync(cancellationToken);
            Assert.DoesNotContain("nobody@example.test", body, StringComparison.Ordinal);
        }

        // Other policies are untouched, and another address is not affected by this one's exhaustion.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, address, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, address, "a@example.test", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ResetAsync(client, address, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "192.0.2.51", cancellationToken)).StatusCode);

        // Each remaining policy exhausts on its own.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, address, cancellationToken)).StatusCode);
        using (var limited = await RefreshAsync(client, address, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            AssertRetryAfterWithin(limited, RefreshWindowSeconds);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, address, "b@example.test", cancellationToken)).StatusCode);
        using (var limited = await ForgotAsync(client, address, "c@example.test", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            AssertRetryAfterWithin(limited, ForgotPasswordWindowSeconds);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await ResetAsync(client, address, cancellationToken)).StatusCode);
        using (var limited = await ResetAsync(client, address, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            AssertRetryAfterWithin(limited, ResetPasswordWindowSeconds);
        }

        // Logout is not one of the limited endpoints.
        for (var i = 0; i < 5; i++)
        {
            using var logout = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/logout");
            logout.Headers.Add(TestConnectionAddressStartupFilter.HeaderName, address);
            using var response = await client.SendAsync(logout, cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var events = factory.CapturedLogs.Where(log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, events.Count);
        Assert.Contains(events, log => log.Contains("'login'", StringComparison.Ordinal) && log.Contains(address, StringComparison.Ordinal));
        Assert.All(events, log => Assert.DoesNotContain("example.test", log, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForgotPasswordIsAlsoLimitedPerNormalizedAddressWhetherOrNotTheAccountExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__ForgotPasswordAddress__PermitLimit"] = "2"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Requests rejected as invalid consume no allowance.
        for (var i = 0; i < 5; i++)
        {
            using var invalid = await PostJsonAsync(client, "/api/auth/forgot-password", "{}", $"203.0.113.{i + 1}", cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        // One mailbox written three ways, from three different origins: the third is limited.
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, "203.0.113.10", "Member@Example.Test", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, "203.0.113.11", "member@example.test", cancellationToken)).StatusCode);
        using var limited = await ForgotAsync(client, "203.0.113.12", "MEMBER@example.test", cancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        AssertRetryAfterWithin(limited, ForgotPasswordAddressWindowSeconds);

        // The same holds for an address with no account: the 429 is no existence oracle.
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, "203.0.113.20", "nobody@example.test", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, "203.0.113.21", "nobody@example.test", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ForgotAsync(client, "203.0.113.22", "nobody@example.test", cancellationToken)).StatusCode);

        // The event names the policy but neither the address nor the client.
        var events = factory.CapturedLogs.Where(log => log.Contains("forgot-password-address", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, log =>
        {
            Assert.StartsWith("RateLimitApplied", log, StringComparison.Ordinal);
            Assert.DoesNotContain("example.test", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("203.0.113", log, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task LockoutAndRequestLimitsNeverTouchEachOther()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Login__PermitLimit"] = "3"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminToken = await LoginTokenAsync(client, AdminAddress, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(factory, adminToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", "Passw0rd!", cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "other@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        // Exhaust the login limit of one address with wrong passwords: three are counted, the rest are rejected
        // before credential checking and so leave the lockout counter alone.
        const string noisy = "192.0.2.60";
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, noisy, cancellationToken, "member@example.test", "Wr0ng-Guess!")).StatusCode);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, noisy, cancellationToken, "member@example.test", "Wr0ng-Guess!")).StatusCode);
        }

        Assert.Equal(3, await FailedCountAsync(factory, "member@example.test"));

        // A different address is judged by the account alone, and success clears the counter.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, "192.0.2.61", cancellationToken, "member@example.test", "Passw0rd!")).StatusCode);
        Assert.Equal(0, await FailedCountAsync(factory, "member@example.test"));

        // Five failures from five different origins lock the account although no origin reached its limit.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, $"192.0.2.7{i}", cancellationToken, "other@example.test", "Wr0ng-Guess!")).StatusCode);
        }

        // A fresh origin, nowhere near a limit, is still refused by the lockout alone.
        using var refused = await LoginAsync(client, "192.0.2.80", cancellationToken, "other@example.test", "Passw0rd!");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("Invalid credentials.", await refused.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    /// <summary>The window the service reports instead of waiting for it to renew (spec NFR-002).</summary>
    private static void AssertRetryAfterWithin(HttpResponseMessage response, int windowSeconds)
    {
        var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
        Assert.NotNull(seconds);
        Assert.InRange(seconds.Value, 1, windowSeconds);
    }

    [Fact]
    public async Task SwitchingRateLimitingOffAppliesNoApplicationLimitAndWarnsAtStartup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Enabled"] = "false",
            ["RateLimiting__Login__PermitLimit"] = "1",
            ["RateLimiting__ForgotPasswordAddress__PermitLimit"] = "1"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Far beyond the configured permit of 1: neither the per-origin nor the per-address limit answers 429.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "192.0.2.77", cancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await ForgotAsync(client, "192.0.2.77", "same@example.test", cancellationToken)).StatusCode);
        }

        Assert.DoesNotContain(factory.CapturedLogs, log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal));
        Assert.Contains(factory.CapturedLogs, log => log.StartsWith("RateLimitingDisabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RateLimitingStaysOnByDefaultAndAnInvalidSwitchStopsStartup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Enabled"] = "",
            ["RateLimiting__Login__PermitLimit"] = "1"
        }))
        using (var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "192.0.2.78", cancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "192.0.2.78", cancellationToken)).StatusCode);
            Assert.DoesNotContain(factory.CapturedLogs, log => log.StartsWith("RateLimitingDisabled", StringComparison.Ordinal));
        }

        using var invalid = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["RateLimiting__Enabled"] = "sometimes"
        });
        var failure = Assert.ThrowsAny<Exception>(() => invalid.CreateClient());
        Assert.Contains("RateLimiting:Enabled", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sometimes", failure.ToString(), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string address, CancellationToken cancellationToken, string email = "nobody@example.test", string password = "Wr0ng-Guess!") =>
        PostJsonAsync(client, "/api/auth/login", System.Text.Json.JsonSerializer.Serialize(new { email, password }), address, cancellationToken);

    private static async Task<string> LoginTokenAsync(HttpClient client, string address, CancellationToken cancellationToken)
    {
        using var response = await LoginAsync(
            client, address, cancellationToken, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginTokenBody>(cancellationToken))!.AccessToken;
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string address, string email, CancellationToken cancellationToken) =>
        PostJsonAsync(client, "/api/auth/forgot-password", System.Text.Json.JsonSerializer.Serialize(new { email }), address, cancellationToken);

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string address, CancellationToken cancellationToken) =>
        PostJsonAsync(
            client,
            "/api/auth/reset-password",
            System.Text.Json.JsonSerializer.Serialize(new { email = "nobody@example.test", token = "not-a-token", newPassword = "N3w-Secret!" }),
            address,
            cancellationToken);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string address, CancellationToken cancellationToken)
    {
        var request = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", "A".PadRight(43, 'A'));
        request.Headers.Add(TestConnectionAddressStartupFilter.HeaderName, address);

        return client.SendAsync(request, cancellationToken);
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string json, string address, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add(TestConnectionAddressStartupFilter.HeaderName, address);

        return client.SendAsync(request, cancellationToken);
    }

    private static async Task<int> FailedCountAsync(AuthenticationApiFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await users.FindByEmailAsync(email))!.AccessFailedCount;
    }
}
