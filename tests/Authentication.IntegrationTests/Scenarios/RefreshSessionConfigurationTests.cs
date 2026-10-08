using System.Net.Http.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RefreshSessionConfigurationTests
{
    [Fact]
    public async Task DefaultLifetimeIsSevenDaysForEveryLoginIssuedSession()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var factory = new AuthenticationApiFactory(timeProvider: new ControlledTimeProvider(now));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var family = Assert.Single(await db.RenewableSessionFamilies.ToListAsync(TestContext.Current.CancellationToken));
        var credential = Assert.Single(await db.RefreshCredentials.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(now.AddDays(7), family.ExpiresAtUtc);
        Assert.Equal(family.ExpiresAtUtc, credential.ExpiresAtUtc);
    }

    [Theory]
    [InlineData(0, AuthenticationApiFactory.FrontendOrigin)]
    [InlineData(int.MaxValue, AuthenticationApiFactory.FrontendOrigin)]
    [InlineData(7, "not-an-origin")]
    public void InvalidSessionConfigurationFailsFastWithoutDisclosingValue(int lifetimeDays, string origin)
    {
        using var factory = new AuthenticationApiFactory(refreshSessionLifetimeDays: lifetimeDays, frontendOrigin: origin);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.DoesNotContain(origin, exception.Message, StringComparison.Ordinal);
    }
}
