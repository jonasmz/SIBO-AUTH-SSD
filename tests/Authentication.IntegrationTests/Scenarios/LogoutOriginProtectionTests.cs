using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class LogoutOriginProtectionTests
{
    [Fact]
    public async Task LogoutRequiresTheConfiguredOriginBeforeProcessingTheCookie()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var credential = await LoginAsync(client);
        foreach (var origin in new string?[] { null, "null", "not-an-origin", "https://other.test" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            request.Headers.Add("Cookie", $"auth_refresh={credential}");
            if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        }
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
    }
}
