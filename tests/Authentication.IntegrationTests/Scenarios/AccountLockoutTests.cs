using System.Net;
using System.Net.Http.Json;
using Authentication.Infrastructure.Identity;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AccountLockoutTests
{
    private const string WrongPassword = "Wr0ng-Guess!";

    [Fact]
    public async Task FiveFailuresLockTheAccountForFifteenMinutesAndTheLockoutExpires()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, WrongPassword, cancellationToken));
        }

        Assert.Equal(4, (await FindAdminAsync(factory)).AccessFailedCount);

        var before = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, WrongPassword, cancellationToken));
        var after = DateTimeOffset.UtcNow;

        // The fifth failure locks: the lockout lies fifteen minutes after the request (bounds, not a duration).
        var locked = await FindAdminAsync(factory);
        Assert.NotNull(locked.LockoutEnd);
        Assert.InRange(locked.LockoutEnd!.Value, before.AddMinutes(15), after.AddMinutes(15));

        // While locked even the correct password gets the same generic failure.
        using var refused = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = AdminTestSupport.AdministratorEmail, password = AdminTestSupport.AdministratorPassword }, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("Invalid credentials.", await refused.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

        // Expiry: Identity reads the wall clock, so the persisted LockoutEnd is moved into the past.
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(AdminTestSupport.AdministratorEmail);
            _ = await users.SetLockoutEndDateAsync(admin!, DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(client, AdminTestSupport.AdministratorPassword, cancellationToken));
        Assert.Equal(0, (await FindAdminAsync(factory)).AccessFailedCount);
    }

    [Fact]
    public async Task ASuccessfulLoginResetsTheCounterAndTheEventsNameTheCauseWithoutSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        _ = await LoginStatusAsync(client, WrongPassword, cancellationToken);
        _ = await LoginStatusAsync(client, WrongPassword, cancellationToken);
        Assert.Equal(2, (await FindAdminAsync(factory)).AccessFailedCount);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(client, AdminTestSupport.AdministratorPassword, cancellationToken));
        Assert.Equal(0, (await FindAdminAsync(factory)).AccessFailedCount);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            _ = await LoginStatusAsync(client, WrongPassword, cancellationToken);
        }

        _ = await LoginStatusAsync(client, AdminTestSupport.AdministratorPassword, cancellationToken);

        var logs = factory.CapturedLogs;
        var adminId = (await FindAdminAsync(factory)).Id;
        Assert.Equal(7, logs.Count(log => log.StartsWith("LoginFailed: reason WrongPassword", StringComparison.Ordinal)));
        Assert.Single(logs, log => log.StartsWith("LoginFailed: reason LockedOut", StringComparison.Ordinal));

        var lockout = Assert.Single(logs, log => log.StartsWith("AccountLockedOut", StringComparison.Ordinal));
        Assert.Contains(adminId, lockout, StringComparison.Ordinal);
        Assert.Contains("source Login", lockout, StringComparison.Ordinal);
        Assert.Matches(@"until \d{4}-\d{2}-\d{2}T[\d:.]+\+00:00", lockout);
        Assert.Matches(@"trace [0-9a-f]{32}", lockout);

        foreach (var secret in new[] { WrongPassword, AdminTestSupport.AdministratorEmail })
        {
            Assert.DoesNotContain(logs, log => log.StartsWith("LoginFailed", StringComparison.Ordinal) && log.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static async Task<HttpStatusCode> LoginStatusAsync(HttpClient client, string password, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = AdminTestSupport.AdministratorEmail, password }, cancellationToken);

        return response.StatusCode;
    }

    private static async Task<ApplicationUser> FindAdminAsync(AuthenticationApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return (await users.FindByEmailAsync(AdminTestSupport.AdministratorEmail))!;
    }
}
