using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Authentication.Infrastructure.Identity;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordResetTests
{
    internal const string ResetPath = "/api/auth/reset-password";
    internal const string Email = "member@example.test";
    internal const string OldPassword = "Passw0rd!";
    internal const string NewPassword = "N3w-Secret!";
    private const string InvalidTokenDetail = "Invalid or expired reset token.";

    [Fact]
    public async Task AValidTokenReplacesThePasswordAndLeavesTheAccountUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateRoleAsync(admin, "Operator", cancellationToken);
        var user = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, ["Operator"], cancellationToken: cancellationToken);
        var before = await LockoutStateAsync(factory);
        var token = await RequestTokenAsync(factory, client, Email, cancellationToken);

        // No access token, cookie or session of any kind accompanies the request.
        using var response = await ResetAsync(client, Email, token, NewPassword, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        await PasswordChangeTests.AssertLoginAsync(client, Email, OldPassword, HttpStatusCode.Unauthorized, cancellationToken);
        await PasswordChangeTests.AssertLoginAsync(client, Email, NewPassword, HttpStatusCode.OK, cancellationToken);

        var after = (await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{user.Id}", cancellationToken))!;
        Assert.Equal(user.Email, after.Email);
        Assert.Equal(user.Enabled, after.Enabled);
        Assert.Equal(user.Roles, after.Roles);
        Assert.Equal(before, await LockoutStateAsync(factory));

        // Neither the token nor a password reaches any log.
        foreach (var secret in new[] { token, OldPassword, NewPassword })
        {
            Assert.DoesNotContain(factory.CapturedLogs, log => log.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task EveryUnusableTokenIsRejectedIdenticallyAndChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "other@example.test", OldPassword, cancellationToken: cancellationToken);
        var disabled = await AdminTestSupport.CreateUserAsync(admin, "disabled@example.test", OldPassword, cancellationToken: cancellationToken);
        var token = await RequestTokenAsync(factory, client, Email, cancellationToken);
        var otherToken = await RequestTokenAsync(factory, client, "other@example.test", cancellationToken);
        var disabledToken = await RequestTokenAsync(factory, client, "disabled@example.test", cancellationToken);
        using (var disable = await admin.PostAsync($"/api/admin/users/{disabled.Id}/disable", null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        }

        // A token issued before the owner changed the password through Phase 5 is no longer valid.
        var staleToken = await RequestTokenAsync(factory, client, "other@example.test", cancellationToken);
        var otherAccess = await AdminTestSupport.LoginAsync(client, "other@example.test", OldPassword, cancellationToken);
        using (var change = await PasswordChangeTests.SendAsync(
            client, PasswordChangeTests.Json(OldPassword, "Changed-Pass1!"), otherAccess, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }

        var cases = new (string Name, string EmailAddress, string Token)[]
        {
            ("garbage", Email, "not-a-token"),
            ("undecodable", Email, "!!!"),
            ("altered", Email, Alter(token)),
            ("another account's token", Email, otherToken),
            ("unknown email", "nobody@example.test", token),
            ("disabled account", "disabled@example.test", disabledToken),
            ("issued before a password change", "other@example.test", staleToken)
        };
        string? reference = null;
        foreach (var (name, emailAddress, candidate) in cases)
        {
            using var response = await ResetAsync(client, emailAddress, candidate, NewPassword, cancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {(int)response.StatusCode}");
            Assert.Empty(response.Headers.WwwAuthenticate);
            var body = StripTraceId(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.Contains(InvalidTokenDetail, body, StringComparison.Ordinal);
            reference ??= body;
            Assert.True(body == reference, $"{name}: the rejection differs from the others");
        }

        // Nothing changed: the passwords still are what they were.
        await PasswordChangeTests.AssertLoginAsync(client, Email, OldPassword, HttpStatusCode.OK, cancellationToken);
        await PasswordChangeTests.AssertLoginAsync(client, Email, NewPassword, HttpStatusCode.Unauthorized, cancellationToken);
        await PasswordChangeTests.AssertLoginAsync(client, "other@example.test", "Changed-Pass1!", HttpStatusCode.OK, cancellationToken);

        // A token works once: after a successful reset the same token is rejected.
        using (var first = await ResetAsync(client, Email, token, NewPassword, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var reuse = await ResetAsync(client, Email, token, "Another-Secret1!", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        await PasswordChangeTests.AssertLoginAsync(client, Email, NewPassword, HttpStatusCode.OK, cancellationToken);
    }

    [Fact]
    public async Task APolicyViolationIsRejectedAndTheTokenStaysUsable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        var token = await RequestTokenAsync(factory, client, Email, cancellationToken);

        using var weak = await ResetAsync(client, Email, token, "abc", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("The password does not satisfy the password policy.", await weak.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        await PasswordChangeTests.AssertLoginAsync(client, Email, OldPassword, HttpStatusCode.OK, cancellationToken);

        using var valid = await ResetAsync(client, Email, token, NewPassword, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }

    [Fact]
    public async Task AnExpiredTokenIsRejectedWithoutWaiting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Tokens issued by this host are already past their validity: no sleeping is needed.
        using var factory = new AuthenticationApiFactory(resetTokenLifespan: TimeSpan.FromMinutes(-1));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, cancellationToken: cancellationToken);
        var token = await RequestTokenAsync(factory, client, Email, cancellationToken);

        using var response = await ResetAsync(client, Email, token, NewPassword, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(InvalidTokenDetail, await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        await PasswordChangeTests.AssertLoginAsync(client, Email, OldPassword, HttpStatusCode.OK, cancellationToken);
    }

    [Fact]
    public async Task UnusableRequestBodiesGetTheInvalidRequestConvention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        foreach (var (content, mediaType) in new (string, string)[]
        {
            ("email=a&token=b", "text/plain"),
            ("{ not json", "application/json"),
            ("""{"email":"member@example.test","token":"t"}""", "application/json"),
            ("""{"email":"member@example.test","newPassword":"N3w-Secret!"}""", "application/json"),
            ("""{"token":"t","newPassword":"N3w-Secret!"}""", "application/json"),
            ("""{"email":"member@example.test","token":" ","newPassword":"N3w-Secret!"}""", "application/json"),
            ("""{"email":"not-an-email","token":"t","newPassword":"N3w-Secret!"}""", "application/json")
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ResetPath)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType)
            };
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The request is invalid.", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        }
    }

    /// <summary>Requests recovery and returns the token carried by the message the host asked to send.</summary>
    internal static async Task<string> RequestTokenAsync(AuthenticationApiFactory factory, HttpClient client, string email, CancellationToken cancellationToken)
    {
        var before = factory.Emails.Messages.Count;
        _ = await PasswordRecoveryRequestTests.ForgotAsync(client, email, cancellationToken);
        var messages = factory.Emails.Messages;
        Assert.Equal(before + 1, messages.Count);

        return CapturingEmailSender.TokenOf(messages[^1]);
    }

    internal static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string token, string newPassword, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(ResetPath, new { email, token, newPassword }, cancellationToken);

    /// <summary>Changes one character in the middle of the token, keeping it a well-formed base64url word.</summary>
    private static string Alter(string token)
    {
        var index = token.Length / 2;
        var replacement = token[index] == 'A' ? 'B' : 'A';

        return string.Concat(token.AsSpan(0, index), replacement.ToString(), token.AsSpan(index + 1));
    }

    private static string StripTraceId(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);

        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => $"{property.Name}={property.Value}"));
    }

    private static async Task<string> LockoutStateAsync(AuthenticationApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(Email);

        return $"{user!.AccessFailedCount}|{user.LockoutEnd}|{user.LockoutEnabled}";
    }
}
