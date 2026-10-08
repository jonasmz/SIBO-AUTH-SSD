using System.Net;
using System.Text.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class BootstrapAndHealthTests
{
    [Fact]
    public async Task StartsFromEmptyStorageWithInitialIdentityStateAndHealthyProbes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        Assert.False(File.Exists(factory.Resources.DatabasePath));

        using var client = factory.CreateClient();

        Assert.True(File.Exists(factory.Resources.DatabasePath));

        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("""{"status":"healthy"}""", body.RootElement.GetRawText());
        }

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<string>>>();

        var role = Assert.Single(await context.Roles.ToListAsync(cancellationToken));
        Assert.Equal(DatabaseInitializer.AdministratorRoleId, role.Id);
        Assert.Equal("Administrator", role.Name);

        var user = Assert.Single(await context.Users.ToListAsync(cancellationToken));
        Assert.Equal(DatabaseInitializer.AdministratorUserId, user.Id);
        Assert.Equal("admin", user.UserName);
        Assert.Equal("admin@local.invalid", user.Email);
        Assert.True(await userManager.CheckPasswordAsync(user, "admin"));
        Assert.Equal(["Administrator"], await userManager.GetRolesAsync(user));
        Assert.Single(await context.UserRoles.ToListAsync(cancellationToken));
    }

    [Fact]
    public void InitializationFailureTerminatesStartupWithoutLeakingConfiguration()
    {
        var unreachable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "secret-auth.db");
        using var factory = new AuthenticationApiFactory($"Data Source={unreachable}");

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var message = string.Join(" | ", Flatten(failure).Select(exception => exception.Message));
        Assert.Contains("initialization failed", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-auth.db", message, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Resources.PrivateKeyPath, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessBecomesUnavailableWithoutLeakingDetailWhenDatabaseDisappears()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using (var ready = await client.GetAsync("/health/ready", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(factory.Resources.DatabasePath + suffix);
        }

        File.WriteAllText(factory.Resources.DatabasePath, "not a sqlite database");

        using var unavailable = await client.GetAsync("/health/ready", cancellationToken);
        var body = await unavailable.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("application/problem+json", unavailable.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("sqlite", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.Resources.RootPath, body, StringComparison.Ordinal);

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            yield return current;
            if (current.InnerException is null)
            {
                yield break;
            }
        }
    }
}
