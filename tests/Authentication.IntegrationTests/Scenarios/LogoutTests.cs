using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class LogoutTests
{
    [Fact]
    public async Task LogoutRevokesTheFamilyClearsCookieAndIsIdempotent()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var credential = await LoginAsync(client);

        using var logout = await SendAsync(client, "/api/auth/logout", credential);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = Assert.Single(logout.Headers.GetValues("Set-Cookie"));
        Assert.Contains("auth_refresh=", cleared, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cleared, StringComparison.OrdinalIgnoreCase);

        using var refresh = await SendAsync(client, "/api/auth/refresh", credential);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.False(refresh.Headers.TryGetValues("Set-Cookie", out _));

        foreach (var unusable in new string?[] { credential, null, "malformed", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" })
        {
            using var repeated = await SendAsync(client, "/api/auth/logout", unusable);
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.True(repeated.Headers.TryGetValues("Set-Cookie", out _));
        }
    }

    [Fact]
    public async Task PersistenceFailureDoesNotClaimLogoutSucceeded()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var credential = await LoginAsync(client);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) File.Delete(factory.Resources.DatabasePath + suffix);
        await File.WriteAllTextAsync(factory.Resources.DatabasePath, "not a sqlite database", TestContext.Current.CancellationToken);

        using var response = await SendAsync(client, "/api/auth/logout", credential);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? credential)
    {
        var request = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, path, credential);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
