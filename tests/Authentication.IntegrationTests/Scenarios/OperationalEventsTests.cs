using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class OperationalEventsTests
{
    private const string MemberEmail = "member@example.test";
    private const string MemberPassword = "Passw0rd!";
    private static readonly string[] SupportOnly = ["Support"];

    [Fact]
    public async Task AdministrativeAndLoginEventsReachTheConsoleAndTheDailyFileWithoutSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();
        string userId;
        string accessToken;
        IReadOnlyList<string> captured;

        // The host is disposed before the file is read: disposing the provider drains its queue.
        using (var factory = new AuthenticationApiFactory(sharedResources: resources))
        {
            using var client = factory.CreateClient();
            var adminToken = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);
            using var admin = AdminTestSupport.WithBearer(factory, adminToken);

            _ = await AdminTestSupport.CreateRoleAsync(admin, "Auditor", cancellationToken);
            _ = await AdminTestSupport.CreateRoleAsync(admin, "Support", cancellationToken);
            var created = await AdminTestSupport.CreateUserAsync(admin, MemberEmail, MemberPassword, ["Auditor"], cancellationToken: cancellationToken);
            userId = created.Id;

            // Replacing Auditor with Support removes one role and assigns another.
            using (var replaced = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/roles", new { roles = SupportOnly }, cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            }

            // Disabling twice changes the state once; enabling restores it.
            foreach (var path in new[] { "disable", "disable", "enable" })
            {
                using var response = await admin.PostAsync($"/api/admin/users/{userId}/{path}", null, cancellationToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            accessToken = await AdminTestSupport.LoginAsync(client, MemberEmail, MemberPassword, cancellationToken);
            captured = factory.CapturedLogs;
        }

        Assert.Single(captured, log => log.StartsWith($"UserCreated: user {userId} with 1 initial roles at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"UserRoleAssigned: user {userId}, role Auditor at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"UserRoleRemoved: user {userId}, role Auditor at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"UserRoleAssigned: user {userId}, role Support at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"UserDisabled: user {userId} at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"UserEnabled: user {userId} at ", StringComparison.Ordinal));
        Assert.Single(captured, log => log.StartsWith($"LoginSucceeded: user {userId} at ", StringComparison.Ordinal));

        var newEvents = captured.Where(log => NewEventPrefixes.Any(prefix => log.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        Assert.All(newEvents, log =>
        {
            Assert.Matches(@" at \d{4}-\d{2}-\d{2}T[\d:.]+\+00:00; trace [0-9a-f]{32}, span [0-9a-f]{16}\.$", log);
            Assert.DoesNotContain(MemberEmail, log, StringComparison.OrdinalIgnoreCase);
        });

        // The same events, and the earlier ones, are in the UTC-dated file of the configured directory.
        var file = Assert.Single(Directory.GetFiles(resources.LogsPath, "auth-*.log"));
        Assert.Equal($"auth-{DateTime.UtcNow:yyyy-MM-dd}.log", Path.GetFileName(file));
        var lines = await File.ReadAllLinesAsync(file, cancellationToken);
        foreach (var expected in newEvents)
        {
            Assert.Contains(lines, line => line.EndsWith(" " + expected, StringComparison.Ordinal));
        }

        Assert.Contains(lines, line => line.Contains(" [Information] Authentication.Infrastructure.Identity.IdentityCredentialValidator", StringComparison.Ordinal)
            && line.Contains(" trace=", StringComparison.Ordinal));

        var content = string.Join('\n', lines);
        foreach (var secret in new[] { MemberPassword, AdminTestSupport.AdministratorPassword + "\"", accessToken, "PRIVATE KEY" })
        {
            Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("unwritable")]
    public void AnUnusableLogDirectoryStopsStartupNamingOnlyTheSetting(string kind)
    {
        // Windows has no Unix mode to make a directory unwritable for the current user.
        Assert.SkipWhen(kind == "unwritable" && OperatingSystem.IsWindows(), "Unix file modes are not available on Windows.");
        using var resources = new Phase1TestResources();
        var directory = kind switch
        {
            "missing" => Path.Combine(resources.RootPath, "no-such-directory"),
            "blank" => " ",
            _ => Path.Combine(resources.RootPath, "read-only")
        };

        if (kind == "unwritable" && !OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        try
        {
            using var factory = new AuthenticationApiFactory(
                sharedResources: resources,
                additionalSettings: new Dictionary<string, string> { ["Logging__File__Directory"] = directory });

            var message = StartupFailure(factory);
            Assert.Contains("'Logging:File:Directory'", message, StringComparison.Ordinal);
            Assert.DoesNotContain(resources.RootPath, message, StringComparison.Ordinal);
        }
        finally
        {
            if (kind == "unwritable" && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("thirty")]
    public void AnInvalidRetentionStopsStartupNamingOnlyTheSetting(string retention)
    {
        using var factory = new AuthenticationApiFactory(
            additionalSettings: new Dictionary<string, string> { ["Logging__File__RetentionDays"] = retention });

        var message = StartupFailure(factory);
        Assert.Contains("'Logging:File:RetentionDays'", message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{retention}'", message, StringComparison.Ordinal);
    }

    private static readonly string[] NewEventPrefixes =
        ["LoginSucceeded:", "UserCreated:", "UserEnabled:", "UserDisabled:", "UserRoleAssigned:", "UserRoleRemoved:"];

    private static string StartupFailure(AuthenticationApiFactory factory)
    {
        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var messages = new List<string>();
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
