using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AdministrativeSessionRevocationTests
{
    [Fact]
    public async Task RevokeSessionsEndsEveryFamilyOfTheUserAndLeavesOthersIntact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminToken = await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(factory, adminToken);
        var user = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        var first = await LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);
        var second = await LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);
        var other = await LoginCookieAsync(browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);

        using var revoke = await admin.PostAsync($"/api/admin/users/{user.Id}/revoke-sessions", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        foreach (var credential in new[] { first, second })
        {
            using var refresh = await RefreshAsync(browser, credential, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.False(refresh.Headers.TryGetValues("Set-Cookie", out _));
        }

        using var unaffected = await RefreshAsync(browser, other, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);

        // Idempotent, and the user can start a brand-new session afterwards.
        using var again = await admin.PostAsync($"/api/admin/users/{user.Id}/revoke-sessions", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var fresh = await LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);
        using var freshRefresh = await RefreshAsync(browser, fresh, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, freshRefresh.StatusCode);
    }

    internal static async Task<string> LoginCookieAsync(HttpClient client, string email, string password, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
    }

    internal static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string credential, CancellationToken cancellationToken) =>
        client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), cancellationToken);
}
