using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AdministrativeSessionRevocationAccessTests
{
    [Fact]
    public async Task RevokeSessionsRequiresAnAdministratorAndAnExistingUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", "Passw0rd!", cancellationToken: cancellationToken);
        using var member = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.LoginAsync(anonymous, "member@example.test", "Passw0rd!", cancellationToken));

        var path = $"/api/admin/users/{user.Id}/revoke-sessions";
        using var unauthenticated = await anonymous.PostAsync(path, null, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var forbidden = await member.PostAsync(path, null, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var missing = await admin.PostAsync("/api/admin/users/does-not-exist/revoke-sessions", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PersistenceFailureAnswersGenericServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) File.Delete(factory.Resources.DatabasePath + suffix);
        await File.WriteAllTextAsync(factory.Resources.DatabasePath, "not a sqlite database", cancellationToken);

        using var response = await admin.PostAsync($"/api/admin/users/{DatabaseId}/revoke-sessions", null, cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("sqlite", body, StringComparison.OrdinalIgnoreCase);
    }

    private const string DatabaseId = Authentication.Infrastructure.Persistence.DatabaseInitializer.AdministratorUserId;
}
