using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RefreshCredentialFailureTests
{
    [Fact]
    public async Task ExpiredCredentialCannotRefresh()
    {
        using var resources = new Phase1TestResources();
        var issuedAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        string credential;
        using (var issuingFactory = new AuthenticationApiFactory(sharedResources: resources, timeProvider: new ControlledTimeProvider(issuedAt)))
        using (var issuingClient = issuingFactory.CreateClient())
        {
            credential = await LoginAsync(issuingClient);
        }
        using var expiredFactory = new AuthenticationApiFactory(sharedResources: resources, timeProvider: new ControlledTimeProvider(issuedAt.AddDays(7)));
        using var expiredClient = expiredFactory.CreateClient();
        using var expired = await RefreshAsync(expiredClient, credential);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.False(expired.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task DisabledAndLockedUsersCannotRefreshAndReceiveTheGenericFailure()
    {
        using var disabledFactory = new AuthenticationApiFactory();
        using var disabledClient = disabledFactory.CreateClient();
        var disabledCredential = await LoginAsync(disabledClient);
        using (var scope = disabledFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).IsEnabled = false;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var disabled = await RefreshAsync(disabledClient, disabledCredential);

        using var lockedFactory = new AuthenticationApiFactory();
        using var lockedClient = lockedFactory.CreateClient();
        var lockedCredential = await LoginAsync(lockedClient);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await lockedClient.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "wrong" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        using var locked = await RefreshAsync(lockedClient, lockedCredential);

        Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.False(disabled.Headers.TryGetValues("Set-Cookie", out _));
        Assert.False(locked.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Equal(
            StripTraceId(await disabled.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)),
            StripTraceId(await locked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string credential) =>
        client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), TestContext.Current.CancellationToken);

    private static string StripTraceId(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => $"{property.Name}={property.Value}"));
    }
}
