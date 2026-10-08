using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Authentication.Infrastructure.Identity;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordChangeTests
{
    internal const string Path = "/api/auth/change-password";
    private const string Email = "member@example.test";
    private const string OldPassword = "Passw0rd!";
    private const string NewPassword = "N3w-Secret!";

    [Fact]
    public async Task UnauthenticatedAndMalformedRequestsChangeNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        var token = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);
        var body = Json(AdminTestSupport.AdministratorPassword, NewPassword);

        // Missing, garbage, and expired access tokens get the established authentication challenge.
        using var expired = new TestTokenMinter(File.ReadAllText(factory.Resources.PrivateKeyPath));
        var expiredToken = expired.Mint(new TestTokenRequest
        {
            IssuedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        foreach (var bearer in new string?[] { null, "garbage", expiredToken })
        {
            using var request = Post(body, bearer);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        }

        // An authenticated caller with an unusable body gets the invalid-request convention.
        var invalidBodies = new (string Content, string MediaType)[]
        {
            ("current=admin&new=x", "text/plain"),
            ("{ not json", "application/json"),
            ("""{"newPassword":"N3w-Secret!"}""", "application/json"),
            ("""{"currentPassword":"admin"}""", "application/json"),
            ("""{"currentPassword":"  ","newPassword":"N3w-Secret!"}""", "application/json")
        };
        foreach (var (content, mediaType) in invalidBodies)
        {
            using var request = Post(content, token, mediaType);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The request is invalid.", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        }

        await AssertLoginAsync(client, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, HttpStatusCode.OK, cancellationToken);
    }

    [Fact]
    public async Task IncorrectCurrentPasswordAndPolicyViolationAreRejectedWithoutChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        var token = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);

        using var wrongCurrent = await SendAsync(client, Json("not-the-password", NewPassword), token, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);
        Assert.Empty(wrongCurrent.Headers.WwwAuthenticate);
        var wrongBody = await wrongCurrent.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("Invalid credentials.", wrongBody, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", wrongBody, StringComparison.Ordinal);

        // An incorrect current password is a failed password attempt counted by Identity (SRS NFR-SEC-BF-001).
        Assert.Equal(1, await AccessFailedCountAsync(factory, cancellationToken));

        // Shorter than the configured minimum length.
        using var weak = await SendAsync(client, Json(AdminTestSupport.AdministratorPassword, "abc"), token, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var weakBody = await weak.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("The password does not satisfy the password policy.", weakBody, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", weakBody.Replace("password", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // A new password that violates the policy is not a failed current-password attempt.
        Assert.Equal(1, await AccessFailedCountAsync(factory, cancellationToken));

        await AssertLoginAsync(client, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, HttpStatusCode.OK, cancellationToken);
        await AssertLoginAsync(client, AdminTestSupport.AdministratorEmail, "abc", HttpStatusCode.Unauthorized, cancellationToken);

        // The configured Identity lockout applies: five consecutive wrong current passwords lock the account.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await SendAsync(client, Json("not-the-password", NewPassword), token, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        await AssertLoginAsync(client, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, HttpStatusCode.Unauthorized, cancellationToken);
    }

    [Fact]
    public async Task ValidChangeReplacesThePasswordAndLeavesTheAccountUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        _ = await AdminTestSupport.CreateRoleAsync(admin, "Operator", cancellationToken);
        var user = await AdminTestSupport.CreateUserAsync(admin, Email, OldPassword, ["Operator"], cancellationToken: cancellationToken);
        var token = await AdminTestSupport.LoginAsync(anonymous, Email, OldPassword, cancellationToken);

        using var response = await SendAsync(anonymous, Json(OldPassword, NewPassword), token, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        await AssertLoginAsync(anonymous, Email, OldPassword, HttpStatusCode.Unauthorized, cancellationToken);
        await AssertLoginAsync(anonymous, Email, NewPassword, HttpStatusCode.OK, cancellationToken);

        var after = (await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{user.Id}", cancellationToken))!;
        Assert.Equal(user.Email, after.Email);
        Assert.Equal(user.Enabled, after.Enabled);
        Assert.Equal(user.Roles, after.Roles);
    }

    [Fact]
    public async Task PersistenceFailureAnswersGenericServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        var token = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(factory.Resources.DatabasePath + suffix);
        }

        await File.WriteAllTextAsync(factory.Resources.DatabasePath, "not a sqlite database", cancellationToken);

        using var response = await SendAsync(client, Json(AdminTestSupport.AdministratorPassword, NewPassword), token, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("The service is not ready.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sqlite", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> AccessFailedCountAsync(AuthenticationApiFactory factory, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(AdminTestSupport.AdministratorEmail);
        cancellationToken.ThrowIfCancellationRequested();

        return admin!.AccessFailedCount;
    }

    internal static string Json(string currentPassword, string newPassword) =>
        System.Text.Json.JsonSerializer.Serialize(new { currentPassword, newPassword });

    internal static HttpRequestMessage Post(string content, string? bearer, string mediaType = "application/json", string? refreshCookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
        };
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"auth_refresh={refreshCookie}");
        }

        return request;
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string content,
        string? bearer,
        CancellationToken cancellationToken,
        string? refreshCookie = null)
    {
        using var request = Post(content, bearer, refreshCookie: refreshCookie);

        return await client.SendAsync(request, cancellationToken);
    }

    internal static async Task AssertLoginAsync(
        HttpClient client,
        string email,
        string password,
        HttpStatusCode expected,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }
}
