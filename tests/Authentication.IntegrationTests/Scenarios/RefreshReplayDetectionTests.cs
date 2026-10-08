using System.Net;
using System.Net.Http.Json;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RefreshReplayDetectionTests
{
    [Fact]
    public async Task ReplayingConsumedCredentialRevokesItsFamilyAndRejectsTheReplacement()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var original = await LoginAsync(client);

        using var rotated = await RefreshAsync(client, original);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var replacement = ExtractCookieValue(rotated.Headers.GetValues("Set-Cookie").Single());

        using var replay = await RefreshAsync(client, original);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.False(replay.Headers.TryGetValues("Set-Cookie", out _));

        using var replacementAttempt = await RefreshAsync(client, replacement);
        Assert.Equal(HttpStatusCode.Unauthorized, replacementAttempt.StatusCode);
        Assert.False(replacementAttempt.Headers.TryGetValues("Set-Cookie", out _));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var family = Assert.Single(await db.RenewableSessionFamilies.ToListAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(family.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.Replay, family.RevocationReason);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return ExtractCookieValue(response.Headers.GetValues("Set-Cookie").Single());
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string credential) =>
        client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), TestContext.Current.CancellationToken);

    private static string ExtractCookieValue(string cookie) => cookie.Split(';', 2)[0].Split('=', 2)[1];
}
