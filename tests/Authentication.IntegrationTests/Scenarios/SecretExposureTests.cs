using System.Net;
using System.Net.Http.Json;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class SecretExposureTests
{
    private const string OldPassword = "Passw0rd!";
    private const string ChangedPassword = "Changed-Secret1!";
    private const string ResetPassword = "Reset-Secret-2!";
    private const string WrongPassword = "Wr0ng-Guess!";
    private const string SmtpPassword = "smtp-secret-pw";

    [Fact]
    public async Task NoFlowLeaksASecretIntoAResponseOrALog()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(additionalSettings: new Dictionary<string, string>
        {
            ["Smtp__Username"] = "smtp-user",
            ["Smtp__Password"] = SmtpPassword,
            ["RateLimiting__Login__PermitLimit"] = "14"
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var responses = new List<string>();
        var issuedCredentials = new List<string>();

        async Task<HttpResponseMessage> Send(HttpRequestMessage request)
        {
            var response = await client.SendAsync(request, cancellationToken);
            var headers = string.Join(";", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
            responses.Add(headers + "|" + await response.Content.ReadAsStringAsync(cancellationToken));

            return response;
        }

        Task<HttpResponseMessage> Login(string email, string password) => Send(new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        });

        using var adminLogin = await Login(AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword);
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<LoginTokenBody>(cancellationToken))!.AccessToken;
        issuedCredentials.Add(adminToken);
        using var admin = AdminTestSupport.WithBearer(factory, adminToken);
        _ = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", OldPassword, cancellationToken: cancellationToken);

        // Login (success).
        using var memberLogin = await Login("member@example.test", OldPassword);
        var memberToken = (await memberLogin.Content.ReadFromJsonAsync<LoginTokenBody>(cancellationToken))!.AccessToken;
        var cookie = Assert.Single(memberLogin.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
        issuedCredentials.AddRange([memberToken, cookie]);
        // Refresh, change-password, logout.
        using var refresh = await Send(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", cookie));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = Assert.Single(refresh.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
        issuedCredentials.Add(rotated);
        using var change = await Send(PasswordChangeTests.Post(
            PasswordChangeTests.Json(AdminTestSupport.AdministratorPassword, ChangedPassword), adminToken, refreshCookie: null));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        using var logout = await Send(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/logout", rotated));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Forgot-password and reset-password with a real token taken from the captured email.
        using var forgot = await Send(new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new { email = AdminTestSupport.AdministratorEmail })
        });
        Assert.Equal(HttpStatusCode.NoContent, forgot.StatusCode);
        var resetToken = CapturingEmailSender.TokenOf(Assert.Single(factory.Emails.Messages));
        using var reset = await Send(new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new { email = AdminTestSupport.AdministratorEmail, token = resetToken, newPassword = ResetPassword })
        });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Failed logins, lockout, and the request limit (a locked account can no longer refresh, so this comes last).
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var failed = await Login("member@example.test", WrongPassword);
        }

        using var limited = await Login("member@example.test", WrongPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);


        // Responses never carry a password, the reset token, key material, or the SMTP secret. Access tokens and the
        // refresh cookie legitimately appear only in the login and refresh responses that issue them.
        var privateKey = await File.ReadAllTextAsync(factory.Resources.PrivateKeyPath, cancellationToken);
        var keyBody = privateKey.Split('\n')[1];
        var neverInResponses = new[] { OldPassword, ChangedPassword, ResetPassword, WrongPassword, AdminTestSupport.AdministratorPassword, SmtpPassword, resetToken, "PRIVATE KEY", keyBody };
        foreach (var secret in neverInResponses)
        {
            Assert.DoesNotContain(responses, response => response.Contains(secret, StringComparison.Ordinal));
        }

        // Logs carry none of those and no issued credential either; rate-limit events name no email.
        var logs = factory.CapturedLogs;
        foreach (var secret in neverInResponses.Concat(issuedCredentials).Append(resetToken))
        {
            Assert.DoesNotContain(logs, log => log.Contains(secret, StringComparison.Ordinal));
        }

        Assert.Contains(logs, log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal));
        Assert.Contains(logs, log => log.StartsWith("AccountLockedOut", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, log => log.StartsWith("RateLimitApplied", StringComparison.Ordinal) && log.Contains("example.test", StringComparison.Ordinal));
    }
}
