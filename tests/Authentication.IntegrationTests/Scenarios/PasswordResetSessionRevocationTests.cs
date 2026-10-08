using System.Net;
using System.Net.Http.Json;
using Authentication.Domain.Sessions;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordResetSessionRevocationTests
{
    private const string Email = PasswordResetTests.Email;
    private const string OldPassword = PasswordResetTests.OldPassword;
    private const string NewPassword = PasswordResetTests.NewPassword;

    [Fact]
    public async Task ASuccessfulResetEndsEveryFamilyOfTheUserAndFailedOnesEndNone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminCookie = await AdministrativeSessionRevocationTests.LoginCookieAsync(
            browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        var cookieA = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, Email, OldPassword, cancellationToken);
        var cookieB = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, Email, OldPassword, cancellationToken);
        var token = await PasswordResetTests.RequestTokenAsync(factory, browser, Email, cancellationToken);

        // Failed resets (unusable token, policy violation) revoke nothing.
        using (var garbage = await PasswordResetTests.ResetAsync(browser, Email, "not-a-token", NewPassword, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
        }

        using (var weak = await PasswordResetTests.ResetAsync(browser, Email, token, "abc", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        }

        Assert.Null((await PasswordChangeSessionRevocationTests.FamilyOfAsync(factory, cookieA, cancellationToken)).RevokedAtUtc);
        Assert.Null((await PasswordChangeSessionRevocationTests.FamilyOfAsync(factory, cookieB, cancellationToken)).RevokedAtUtc);

        using var reset = await PasswordResetTests.ResetAsync(browser, Email, token, NewPassword, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Both families ended because of the reset, none was kept, and another user's family is untouched.
        foreach (var cookie in new[] { cookieA, cookieB })
        {
            var family = await PasswordChangeSessionRevocationTests.FamilyOfAsync(factory, cookie, cancellationToken);
            Assert.NotNull(family.RevokedAtUtc);
            Assert.Equal(SessionRevocationReason.PasswordReset, family.RevocationReason);
            using var refresh = await AdministrativeSessionRevocationTests.RefreshAsync(browser, cookie, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        using var adminRefresh = await AdministrativeSessionRevocationTests.RefreshAsync(browser, adminCookie, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminRefresh.StatusCode);

        // One security event: user id, revoked count, UTC time, correlation; no secret in any log.
        var logs = factory.CapturedLogs;
        var resetEvent = Assert.Single(logs, log => log.StartsWith("PasswordReset:", StringComparison.Ordinal));
        Assert.Contains(user.Id, resetEvent, StringComparison.Ordinal);
        Assert.Contains("2 renewable session families revoked", resetEvent, StringComparison.Ordinal);
        Assert.Matches(@"at \d{4}-\d{2}-\d{2}T[\d:.]+\+00:00", resetEvent);
        Assert.Matches(@"trace [0-9a-f]{32}", resetEvent);
        foreach (var secret in new[] { token, OldPassword, NewPassword, cookieA, cookieB })
        {
            Assert.DoesNotContain(logs, log => log.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("AspNetUsers")]
    [InlineData("RenewableSessionFamilies")]
    public async Task AFailureWhileWritingEitherPartRollsBackTheWholeReset(string table)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        var cookieA = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, Email, OldPassword, cancellationToken);
        var cookieB = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, Email, OldPassword, cancellationToken);
        var token = await PasswordResetTests.RequestTokenAsync(factory, browser, Email, cancellationToken);

        // A temporary trigger makes the write to one table fail, as an unavailable database would.
        await ExecuteAsync(factory, $"CREATE TRIGGER fail_reset BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'injected failure'); END;", cancellationToken);
        try
        {
            using var failed = await PasswordResetTests.ResetAsync(browser, Email, token, NewPassword, cancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            var body = await failed.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("The service is not ready.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("injected", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ExecuteAsync(factory, "DROP TRIGGER fail_reset;", cancellationToken);
        }

        // Nothing changed: the old password works, both families are active, and the token is still good.
        await PasswordChangeTests.AssertLoginAsync(browser, Email, OldPassword, HttpStatusCode.OK, cancellationToken);
        Assert.Null((await PasswordChangeSessionRevocationTests.FamilyOfAsync(factory, cookieA, cancellationToken)).RevokedAtUtc);
        Assert.Null((await PasswordChangeSessionRevocationTests.FamilyOfAsync(factory, cookieB, cancellationToken)).RevokedAtUtc);
        using var retry = await PasswordResetTests.ResetAsync(browser, Email, token, NewPassword, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    private static async Task ExecuteAsync(AuthenticationApiFactory factory, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={factory.Resources.DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
