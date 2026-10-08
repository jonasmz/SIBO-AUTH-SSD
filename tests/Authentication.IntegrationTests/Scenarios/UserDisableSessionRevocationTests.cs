using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class UserDisableSessionRevocationTests
{
    [Fact]
    public async Task DisablingRevokesEveryFamilyAndEnablingNeverRestoresThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(admin, "member@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        var first = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);
        var second = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);

        using var disable = await admin.PostAsync($"/api/admin/users/{user.Id}/disable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        using var enable = await admin.PostAsync($"/api/admin/users/{user.Id}/enable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        foreach (var credential in new[] { first, second })
        {
            using var refresh = await AdministrativeSessionRevocationTests.RefreshAsync(browser, credential, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        var fresh = await AdministrativeSessionRevocationTests.LoginCookieAsync(browser, "member@example.test", "Passw0rd!", cancellationToken);
        using var freshRefresh = await AdministrativeSessionRevocationTests.RefreshAsync(browser, fresh, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, freshRefresh.StatusCode);

        // Enabling an already-enabled user must not revoke the active family.
        using var enableAgain = await admin.PostAsync($"/api/admin/users/{user.Id}/enable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enableAgain.StatusCode);
        var rotated = freshRefresh.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];
        using var stillValid = await AdministrativeSessionRevocationTests.RefreshAsync(browser, rotated, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }
}
