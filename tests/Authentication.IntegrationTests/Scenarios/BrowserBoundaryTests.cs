using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class BrowserBoundaryTests
{
    [Fact]
    public async Task TheRefreshCookieIsHardenedAndLogoutClearsItWithTheSameAttributes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(environment: "Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = AdminTestSupport.AdministratorEmail, password = AdminTestSupport.AdministratorPassword }, cancellationToken);
        var issued = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        AssertHardened(issued);
        var cookie = issued.Split(';', 2)[0].Split('=', 2)[1];

        using var refresh = await client.SendAsync(
            AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", cookie), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = Assert.Single(refresh.Headers.GetValues("Set-Cookie"));
        AssertHardened(rotated);
        var current = rotated.Split(';', 2)[0].Split('=', 2)[1];

        using var logout = await client.SendAsync(
            AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/logout", current), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        AssertHardened(cleared);
        Assert.StartsWith("auth_refresh=;", cleared, StringComparison.Ordinal);
        Assert.Matches("(?i)expires=Thu, 01 Jan 1970|max-age=0", cleared);

        using var afterLogout = await client.SendAsync(
            AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", current), cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task OnlyTheConfiguredOriginIsAcceptedAndNoCrossOriginSharingIsEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = AdminTestSupport.AdministratorEmail, password = AdminTestSupport.AdministratorPassword }, cancellationToken);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];

        foreach (var path in new[] { "/api/auth/refresh", "/api/auth/logout" })
        {
            foreach (var origins in new string[]?[] { null, ["https://other.test"], ["not an origin"], [AuthenticationApiFactory.FrontendOrigin, "https://other.test"] })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, path);
                request.Headers.Add("Cookie", $"auth_refresh={cookie}");
                if (origins is not null)
                {
                    request.Headers.TryAddWithoutValidation("Origin", origins);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{path}: {string.Join(",", origins ?? [])} -> {(int)response.StatusCode}");
                Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
            }
        }

        // The rejections happened before the cookie was processed: it still rotates, with the configured origin.
        using var accepted = await client.SendAsync(
            AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", cookie), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // A preflight from a foreign origin gets no cross-origin permission, and the service has no CORS machinery.
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        preflight.Headers.Add("Origin", "https://other.test");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var preflightResponse = await client.SendAsync(preflight, cancellationToken);
        Assert.DoesNotContain(preflightResponse.Headers, header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
        Assert.Null(factory.Services.GetService<ICorsService>());
    }

    private static void AssertHardened(string setCookie)
    {
        Assert.Contains("auth_refresh=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
