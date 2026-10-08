using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordRecoveryRestartTests
{
    private const string Email = PasswordResetTests.Email;

    [Fact]
    public async Task AStillValidTokenSurvivesARestartOnlyWhileTheKeyRingIsTheSame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();
        var otherKeys = Path.Combine(resources.RootPath, "other-dataprotection");
        Directory.CreateDirectory(otherKeys);

        // Hosts stay alive together: they share one JWT key pair and start (initialization included)
        // against the database and key directory the first one wrote.
        using var original = new AuthenticationApiFactory(sharedResources: resources);
        using var originalClient = original.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(
            original, await AdminTestSupport.AdministratorTokenAsync(originalClient, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, Email, PasswordResetTests.OldPassword, cancellationToken: cancellationToken);
        var token = await PasswordResetTests.RequestTokenAsync(original, originalClient, Email, cancellationToken);
        var otherToken = await PasswordResetTests.RequestTokenAsync(original, originalClient, Email, cancellationToken);

        // Negative control: a restart that lost the key ring cannot verify the token.
        using var withoutKeys = new AuthenticationApiFactory(sharedResources: resources, dataProtectionKeysPath: otherKeys);
        using var withoutKeysClient = withoutKeys.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using (var rejected = await PasswordResetTests.ResetAsync(withoutKeysClient, Email, otherToken, PasswordResetTests.NewPassword, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        await PasswordChangeTests.AssertLoginAsync(
            withoutKeysClient, Email, PasswordResetTests.OldPassword, HttpStatusCode.OK, cancellationToken);

        // A restart on the same SQLite file and key directory keeps the outstanding token valid.
        using var restarted = new AuthenticationApiFactory(sharedResources: resources);
        using var restartedClient = restarted.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var accepted = await PasswordResetTests.ResetAsync(restartedClient, Email, token, PasswordResetTests.NewPassword, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        await PasswordChangeTests.AssertLoginAsync(
            restartedClient, Email, PasswordResetTests.NewPassword, HttpStatusCode.OK, cancellationToken);
    }
}
