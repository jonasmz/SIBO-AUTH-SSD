using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RenewableLoginSessionTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulLoginCreatesOneFamilyAndRestrictiveCookie()
    {
        using var factory = new AuthenticationApiFactory(timeProvider: new ControlledTimeProvider(Now));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["accessToken", "expiresAtUtc"], body.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("auth_refresh=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var family = Assert.Single(await db.RenewableSessionFamilies.ToListAsync(TestContext.Current.CancellationToken));
        var credential = Assert.Single(await db.RefreshCredentials.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Now.AddDays(7), family.ExpiresAtUtc);
        Assert.Equal(family.ExpiresAtUtc, credential.ExpiresAtUtc);
        Assert.Equal(32, credential.TokenHash.Length);
        Assert.DoesNotContain(ExtractCookieValue(cookie), Convert.ToBase64String(credential.TokenHash), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDisabledAndLockedLoginsCreateNoSession()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        await AssertNoSessionAsync(client, factory, "missing@local.invalid", "admin");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var admin = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
            admin.IsEnabled = false;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await AssertNoSessionAsync(client, factory, "admin@local.invalid", "admin");

        using var lockedFactory = new AuthenticationApiFactory();
        using var lockedClient = lockedFactory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await lockedClient.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "wrong" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        await AssertNoSessionAsync(lockedClient, lockedFactory, "admin@local.invalid", "admin");
    }

    private static async Task AssertNoSessionAsync(HttpClient client, AuthenticationApiFactory factory, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        Assert.Empty(await db.RenewableSessionFamilies.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static string ExtractCookieValue(string cookie) => cookie.Split(';', 2)[0].Split('=', 2)[1];
}
