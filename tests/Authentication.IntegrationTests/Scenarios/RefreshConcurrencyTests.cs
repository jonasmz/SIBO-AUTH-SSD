using System.Net;
using System.Net.Http.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RefreshConcurrencyTests
{
    [Fact]
    public async Task ConcurrentRequestsCannotCreateIndependentContinuations()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var credential = await LoginAsync(client);

        var first = client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), TestContext.Current.CancellationToken);
        var second = client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.InRange(responses.Count(response => response.StatusCode == HttpStatusCode.OK), 0, 1);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var credentials = await db.RefreshCredentials.ToListAsync(TestContext.Current.CancellationToken);
            Assert.InRange(credentials.Count, 1, 2);
            Assert.InRange(credentials.Count(item => item.ConsumedAtUtc is null), 0, 1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
    }
}
