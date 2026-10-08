using System.Net;
using System.Net.Http.Json;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordChangeSessionRevocationTests
{
    private const string Email = "member@example.test";
    private const string OldPassword = "Passw0rd!";
    private const string NewPassword = "N3w-Secret!";

    [Fact]
    public async Task ChangeKeepsOnlyTheSessionWhoseCookieAccompaniesItAndLogsASecretFreeEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        var (_, cookieA) = await SignInAsync(browser, Email, OldPassword, cancellationToken);
        var (tokenB, cookieB) = await SignInAsync(browser, Email, OldPassword, cancellationToken);

        using var response = await PasswordChangeTests.SendAsync(
            browser, PasswordChangeTests.Json(OldPassword, NewPassword), tokenB, cancellationToken, refreshCookie: cookieA);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));

        // Persisted state: A is untouched, B ended because of the password change.
        Assert.Null((await FamilyOfAsync(factory, cookieA, cancellationToken)).RevokedAtUtc);
        var familyB = await FamilyOfAsync(factory, cookieB, cancellationToken);
        Assert.NotNull(familyB.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.PasswordChanged, familyB.RevocationReason);

        using var refreshA = await RefreshAsync(browser, cookieA, cancellationToken);
        using var refreshB = await RefreshAsync(browser, cookieB, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshA.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshB.StatusCode);

        // Exactly one security event: user id, revoked count, UTC time, correlation; no secrets anywhere.
        var logs = factory.CapturedLogs;
        var changeEvent = Assert.Single(logs, message => message.StartsWith("Password changed for user", StringComparison.Ordinal));
        Assert.Contains(user.Id, changeEvent, StringComparison.Ordinal);
        Assert.Contains("1 other renewable session families revoked", changeEvent, StringComparison.Ordinal);
        Assert.Matches(@"at \d{4}-\d{2}-\d{2}T[\d:.]+\+00:00", changeEvent);
        Assert.Matches(@"trace [0-9a-f]{32}", changeEvent);
        foreach (var secret in new[] { OldPassword, NewPassword, cookieA, cookieB, tokenB })
        {
            Assert.DoesNotContain(logs, message => message.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task WithoutAUsableCookieEveryFamilyOfTheUserEndsAndOtherUsersAreUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (adminToken, adminCookie) = await SignInAsync(
            browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(factory, adminToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);

        var current = OldPassword;
        var round = 0;
        foreach (var kind in new[] { "absent", "malformed", "unknown", "revoked", "other-user" })
        {
            var (token, first) = await SignInAsync(browser, Email, current, cancellationToken);
            var (_, second) = await SignInAsync(browser, Email, current, cancellationToken);
            string? cookie = kind switch
            {
                "absent" => null,
                "malformed" => "not-a-credential",
                "unknown" => new RefreshCredentialProtector().CreateRawCredential(),
                "revoked" => await LogOutAsync(browser, first, cancellationToken),
                _ => adminCookie
            };

            var next = $"Round-{round++}-Secret!";
            using var response = await PasswordChangeTests.SendAsync(
                browser, PasswordChangeTests.Json(current, next), token, cancellationToken, refreshCookie: cookie);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            current = next;

            foreach (var credential in new[] { first, second })
            {
                using var refresh = await RefreshAsync(browser, credential, cancellationToken);
                Assert.True(refresh.StatusCode == HttpStatusCode.Unauthorized, $"{kind}: a family survived");
            }
        }

        // The administrator's own family never belonged to the changing user.
        using var adminRefresh = await RefreshAsync(browser, adminCookie, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminRefresh.StatusCode);
    }

    [Fact]
    public async Task AnExpiredCookieKeepsNothingWhileAnActiveFamilyIsRevoked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();
        var start = DateTimeOffset.UtcNow;

        // All hosts stay alive together: they share the key material and the SQLite file.
        using var first = new AuthenticationApiFactory(sharedResources: resources, timeProvider: new ControlledTimeProvider(start));
        using var second = new AuthenticationApiFactory(sharedResources: resources, timeProvider: new ControlledTimeProvider(start.AddDays(6)));
        using var firstBrowser = first.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var secondBrowser = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (token, expiredCookie) = await SignInAsync(
            firstBrowser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);
        var (_, activeCookie) = await SignInAsync(
            secondBrowser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);

        // Eight days after the first login its family has expired, while the second is still active.
        using var third = new AuthenticationApiFactory(sharedResources: resources, timeProvider: new ControlledTimeProvider(start.AddDays(8)));
        using var client = third.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var response = await PasswordChangeTests.SendAsync(
            client, PasswordChangeTests.Json(AdminTestSupport.AdministratorPassword, NewPassword), token, cancellationToken, refreshCookie: expiredCookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var refresh = await RefreshAsync(client, activeCookie, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task FailedChangesRevokeNoSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (token, cookieA) = await SignInAsync(
            browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);
        var (_, cookieB) = await SignInAsync(
            browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);

        var attempts = new (string Body, string? Bearer, HttpStatusCode Expected)[]
        {
            (PasswordChangeTests.Json(AdminTestSupport.AdministratorPassword, NewPassword), null, HttpStatusCode.Unauthorized),
            (PasswordChangeTests.Json("wrong-current", NewPassword), token, HttpStatusCode.Unauthorized),
            (PasswordChangeTests.Json(AdminTestSupport.AdministratorPassword, "abc"), token, HttpStatusCode.BadRequest),
            ("{ not json", token, HttpStatusCode.BadRequest)
        };
        foreach (var (body, bearer, expected) in attempts)
        {
            using var response = await PasswordChangeTests.SendAsync(browser, body, bearer, cancellationToken, refreshCookie: cookieA);
            Assert.Equal(expected, response.StatusCode);
        }

        foreach (var cookie in new[] { cookieA, cookieB })
        {
            using var refresh = await RefreshAsync(browser, cookie, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        }
    }

    private static async Task<(string Token, string Cookie)> SignInAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<LoginTokenBody>(cancellationToken))!;

        return (body.AccessToken, response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1]);
    }

    private static async Task<string> LogOutAsync(HttpClient client, string cookie, CancellationToken cancellationToken)
    {
        using var request = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/logout", cookie);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return cookie;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie, CancellationToken cancellationToken) =>
        client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", cookie), cancellationToken);

    internal static async Task<RenewableSessionFamily> FamilyOfAsync(AuthenticationApiFactory factory, string cookie, CancellationToken cancellationToken)
    {
        Assert.True(RefreshCredentialProtector.TryHash(cookie, out var hash));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var credential = await context.RefreshCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.TokenHash.SequenceEqual(hash), cancellationToken);

        return await context.RenewableSessionFamilies.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == credential.FamilyId, cancellationToken);
    }
}
