using System.Net;
using System.Net.Http.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AdministratorPasswordChangeTests
{
    private const string ReplacementPassword = "Retired-Admin-1!";

    [Fact]
    public async Task TheInitialAdministratorRetiresTheDefaultPasswordAndARestartDoesNotRestoreIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();

        // Both hosts stay alive together: they share one key pair, and the second one starts
        // (initialization and bootstrap included) against the database the first one wrote.
        using var original = new AuthenticationApiFactory(sharedResources: resources);
        using var client = original.CreateClient();
        var token = await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken);

        // The same endpoint and code path as for any user; no email is configured or needed.
        using var change = await PasswordChangeTests.SendAsync(
            client, PasswordChangeTests.Json(AdminTestSupport.AdministratorPassword, ReplacementPassword), token, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        await PasswordChangeTests.AssertLoginAsync(
            client, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, HttpStatusCode.Unauthorized, cancellationToken);
        await PasswordChangeTests.AssertLoginAsync(
            client, AdminTestSupport.AdministratorEmail, ReplacementPassword, HttpStatusCode.OK, cancellationToken);

        // Role and enabled state are untouched, so the last-administrator protection is unaffected.
        using var admin = AdminTestSupport.WithBearer(
            original, await AdminTestSupport.LoginAsync(client, AdminTestSupport.AdministratorEmail, ReplacementPassword, cancellationToken));
        var account = (await admin.GetFromJsonAsync<AdminUserBody>(
            $"/api/admin/users/{DatabaseInitializer.AdministratorUserId}", cancellationToken))!;
        Assert.True(account.Enabled);
        Assert.Equal(["Administrator"], account.Roles);
        using var disable = await admin.PostAsync($"/api/admin/users/{DatabaseInitializer.AdministratorUserId}/disable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode);

        // A restarted Authentication API (initialization against the same file) keeps the new secret.
        using var restarted = new AuthenticationApiFactory(sharedResources: resources);
        using var restartedClient = restarted.CreateClient();
        await PasswordChangeTests.AssertLoginAsync(
            restartedClient, AdminTestSupport.AdministratorEmail, ReplacementPassword, HttpStatusCode.OK, cancellationToken);
        await PasswordChangeTests.AssertLoginAsync(
            restartedClient, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, HttpStatusCode.Unauthorized, cancellationToken);
    }
}
