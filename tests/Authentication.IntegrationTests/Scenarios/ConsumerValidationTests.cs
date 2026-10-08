using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class ConsumerValidationTests
{
    private sealed record CallerBody(string Service, string Subject, string[] Roles);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    [Fact]
    public async Task TokenIssuedByAuthenticationApiIsAcceptedLocallyByBothConsumersEvenWhenItIsStopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var auth = new AuthenticationApiFactory();
        using var authClient = auth.CreateClient();
        using var login = await authClient.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "admin@local.invalid", password = "admin" },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginBody>(cancellationToken))!.AccessToken;

        // Both consumers receive only the public half of the pair Authentication API signs with.
        using var apiA = new ReferenceConsumerFactory("api-a", auth.Resources.PublicKeyPem);
        using var apiB = new ReferenceConsumerFactory("api-b", auth.Resources.PublicKeyPem);
        using var clientA = apiA.CreateClient();
        using var clientB = apiB.CreateClient();

        await AssertCallerAsync(clientA, token, "api-a", cancellationToken);
        await AssertCallerAsync(clientB, token, "api-b", cancellationToken);

        // Authentication API goes away; still-valid tokens keep working because validation is local.
        authClient.Dispose();
        auth.Dispose();

        await AssertCallerAsync(clientA, token, "api-a", cancellationToken);
        await AssertCallerAsync(clientB, token, "api-b", cancellationToken);
    }

    [Fact]
    public async Task ConsumersKeepAcceptingAnUnexpiredTokenAfterEverySessionRevocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var auth = new AuthenticationApiFactory();
        using var browser = auth.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminToken = await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(auth, adminToken);
        var user = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        using var apiA = new ReferenceConsumerFactory("api-a", auth.Resources.PublicKeyPem);
        using var apiB = new ReferenceConsumerFactory("api-b", auth.Resources.PublicKeyPem);
        using var clientA = apiA.CreateClient();
        using var clientB = apiB.CreateClient();

        async Task<(string Token, string Cookie)> SignInAsync()
        {
            using var response = await browser.PostAsJsonAsync(
                "/api/auth/login", new { email = "member@example.test", password = "Passw0rd!" }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = (await response.Content.ReadFromJsonAsync<LoginBody>(cancellationToken))!.AccessToken;
            return (token, response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1]);
        }

        async Task<HttpStatusCode> PostAsync(string path, string? cookie)
        {
            using var request = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, path, cookie);
            using var response = await browser.SendAsync(request, cancellationToken);
            return response.StatusCode;
        }

        async Task AssertStillAcceptedAsync(string token)
        {
            foreach (var (client, service) in new[] { (clientA, "api-a"), (clientB, "api-b") })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(request, cancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{service} rejected a pre-revocation token");
            }
        }

        // Logout.
        var (logoutToken, logoutCookie) = await SignInAsync();
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync("/api/auth/logout", logoutCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("/api/auth/refresh", logoutCookie));
        await AssertStillAcceptedAsync(logoutToken);

        // Replay: reusing a consumed credential revokes the family.
        var (replayToken, replayCookie) = await SignInAsync();
        Assert.Equal(HttpStatusCode.OK, await PostAsync("/api/auth/refresh", replayCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("/api/auth/refresh", replayCookie));
        await AssertStillAcceptedAsync(replayToken);

        // Administrative revocation.
        var (adminRevokedToken, adminRevokedCookie) = await SignInAsync();
        using (var revoke = await admin.PostAsync($"/api/admin/users/{user.Id}/revoke-sessions", null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("/api/auth/refresh", adminRevokedCookie));
        await AssertStillAcceptedAsync(adminRevokedToken);

        // Disablement.
        var (disabledToken, disabledCookie) = await SignInAsync();
        using (var disable = await admin.PostAsync($"/api/admin/users/{user.Id}/disable", null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync("/api/auth/refresh", disabledCookie));
        await AssertStillAcceptedAsync(disabledToken);
    }

    [Theory]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", " ")]
    [InlineData("Jwt:ClockSkewSeconds", "")]
    [InlineData("Jwt:ClockSkewSeconds", "not-a-number")]
    [InlineData("Jwt:ClockSkewSeconds", "-1")]
    [InlineData("Jwt:ClockSkewSeconds", "61")]
    public void InvalidValidationSettingTerminatesStartupNamingOnlyTheSetting(string setting, string value)
    {
        using var minter = new TestTokenMinter();
        using var factory = new ReferenceConsumerFactory(
            "api-a",
            minter.PublicKeyPem,
            new Dictionary<string, string> { [setting] = value });

        var message = StartupFailureMessage(factory);

        Assert.Contains(setting, message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.PublicKeyPath, message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPublicKeyFileTerminatesStartupWithoutRevealingThePath()
    {
        using var minter = new TestTokenMinter();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secret-public.pem");
        using var factory = new ReferenceConsumerFactory(
            "api-a",
            minter.PublicKeyPem,
            new Dictionary<string, string> { ["Jwt:PublicKeyPath"] = missingPath });

        var message = StartupFailureMessage(factory);

        Assert.Contains("Jwt:PublicKeyPath", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-public.pem", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateKeySuppliedAsThePublicKeyTerminatesStartupWithoutLeakingKeyMaterial()
    {
        using var minter = new TestTokenMinter();
        var privatePem = minter.PrivateKeyPem;
        var body = privatePem.Split('\n')[1];
        using var factory = new ReferenceConsumerFactory("api-a", privatePem);

        var message = StartupFailureMessage(factory);

        Assert.Contains("Jwt:PublicKeyPath", message, StringComparison.Ordinal);
        Assert.DoesNotContain(body, message, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.PublicKeyPath, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCredentialsAreRejectedWith401AndNoDetailByBothConsumers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var minter = new TestTokenMinter();
        using var unrelated = new TestTokenMinter();
        using var apiA = new ReferenceConsumerFactory("api-a", minter.PublicKeyPem);
        using var apiB = new ReferenceConsumerFactory("api-b", minter.PublicKeyPem);
        var now = DateTimeOffset.UtcNow;

        // Expiry offsets sit far from the 30 second tolerance so no sleeps or clock control are needed.
        var cases = new (string Name, string? Authorization)[]
        {
            ("no credential", null),
            ("non-bearer scheme", "Basic dXNlcjpwYXNz"),
            ("malformed token", "Bearer not-a-jwt"),
            ("forged signature", Bearer(unrelated.Mint(new TestTokenRequest()))),
            ("unsigned token", Bearer(minter.Mint(new TestTokenRequest { Algorithm = TestTokenAlgorithm.None }))),
            ("non-RS256 algorithm", Bearer(minter.Mint(new TestTokenRequest { Algorithm = TestTokenAlgorithm.Rs512 }))),
            ("expired beyond tolerance", Bearer(minter.Mint(Expired(now, TimeSpan.FromSeconds(90))))),
            ("wrong issuer", Bearer(minter.Mint(new TestTokenRequest { Issuer = "https://other-issuer.test" }))),
            ("wrong audience", Bearer(minter.Mint(new TestTokenRequest { Audience = "other-audience" })))
        };

        foreach (var (service, factory) in new[] { ("api-a", apiA), ("api-b", apiB) })
        {
            using var client = factory.CreateClient();

            foreach (var (name, authorization) in cases)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
                if (authorization is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authorization);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var challenge = Assert.Single(response.Headers.WwwAuthenticate);

                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{service}: {name} -> {(int)response.StatusCode}");
                Assert.True(body.Length == 0, $"{service}: {name} returned a body");
                Assert.True(
                    challenge.Scheme == "Bearer" && string.IsNullOrEmpty(challenge.Parameter),
                    $"{service}: {name} leaked challenge detail");
            }
        }
    }

    [Fact]
    public async Task ExpiryHonorsTheConfiguredClockTolerance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var minter = new TestTokenMinter();
        using var factory = new ReferenceConsumerFactory("api-a", minter.PublicKeyPem);
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;

        using var withinTolerance = await GetCallerAsync(
            client, minter.Mint(Expired(now, TimeSpan.FromSeconds(10))), cancellationToken);
        using var beyondTolerance = await GetCallerAsync(
            client, minter.Mint(Expired(now, TimeSpan.FromSeconds(90))), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, withinTolerance.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, beyondTolerance.StatusCode);
    }

    [Fact]
    public async Task AdministratorEndpointRequiresTheAdministratorRoleOnBothConsumers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var minter = new TestTokenMinter();
        using var apiA = new ReferenceConsumerFactory("api-a", minter.PublicKeyPem);
        using var apiB = new ReferenceConsumerFactory("api-b", minter.PublicKeyPem);

        foreach (var (service, factory) in new[] { ("api-a", apiA), ("api-b", apiB) })
        {
            using var client = factory.CreateClient();

            // Single role and multiple roles including Administrator are both authorized.
            foreach (var roles in new[] { new[] { "Administrator" }, ["Operator", "Administrator"] })
            {
                using var allowed = await GetAsync(
                    client, "/api/caller/administrator", minter.Mint(new TestTokenRequest { Roles = roles }), cancellationToken);
                Assert.True(allowed.StatusCode == HttpStatusCode.OK, $"{service}: {string.Join(",", roles)}");
                var caller = await allowed.Content.ReadFromJsonAsync<CallerBody>(cancellationToken);
                Assert.Equal(service, caller!.Service);
                Assert.Equal(roles, caller.Roles);
            }

            // A valid token without the role is authenticated (200 on the caller endpoint) but forbidden.
            foreach (var roles in new[] { new[] { "Operator" }, Array.Empty<string>() })
            {
                var token = minter.Mint(new TestTokenRequest { Roles = roles });

                using var authenticated = await GetAsync(client, "/api/caller", token, cancellationToken);
                using var forbidden = await GetAsync(client, "/api/caller/administrator", token, cancellationToken);

                Assert.True(authenticated.StatusCode == HttpStatusCode.OK, $"{service}: caller with [{string.Join(",", roles)}]");
                Assert.True(forbidden.StatusCode == HttpStatusCode.Forbidden, $"{service}: admin with [{string.Join(",", roles)}]");
                Assert.Empty(await forbidden.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            // No valid token is 401, never 403.
            using var anonymous = await client.GetAsync("/api/caller/administrator", cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, cancellationToken);
    }

    private static TestTokenRequest Expired(DateTimeOffset now, TimeSpan expiredFor) => new()
    {
        IssuedAt = now - expiredFor - TimeSpan.FromMinutes(15),
        ExpiresAt = now - expiredFor
    };

    private static string Bearer(string token) => $"Bearer {token}";

    private static async Task<HttpResponseMessage> GetCallerAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task AssertCallerAsync(
        HttpClient client,
        string token,
        string expectedService,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var caller = await response.Content.ReadFromJsonAsync<CallerBody>(cancellationToken);
        Assert.NotNull(caller);
        Assert.Equal(expectedService, caller.Service);
        Assert.Equal(DatabaseInitializer.AdministratorUserId, caller.Subject);
        Assert.Equal(["Administrator"], caller.Roles);
    }

    private static string StartupFailureMessage(ReferenceConsumerFactory factory)
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
