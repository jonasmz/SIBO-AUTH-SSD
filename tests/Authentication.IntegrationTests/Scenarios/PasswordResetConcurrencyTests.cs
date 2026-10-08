using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordResetConcurrencyTests
{
    [Fact]
    public async Task TwoSimultaneousResetsWithOneTokenYieldExactlyOneSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(client, cancellationToken));
        _ = await AdminTestSupport.CreateUserAsync(admin, PasswordResetTests.Email, PasswordResetTests.OldPassword, cancellationToken: cancellationToken);

        for (var round = 0; round < 4; round++)
        {
            var token = await PasswordResetTests.RequestTokenAsync(factory, client, PasswordResetTests.Email, cancellationToken);
            string[] candidates = [$"Round-{round}-First!", $"Round-{round}-Second!"];
            var gate = new TaskCompletionSource();
            var attempts = candidates.Select(candidate => Task.Run(async () =>
            {
                await gate.Task;
                using var response = await PasswordResetTests.ResetAsync(client, PasswordResetTests.Email, token, candidate, cancellationToken);
                return response.StatusCode;
            }, cancellationToken)).ToArray();
            gate.SetResult();
            var statuses = await Task.WhenAll(attempts);

            Assert.True(
                statuses.Order().SequenceEqual([HttpStatusCode.NoContent, HttpStatusCode.Unauthorized]),
                $"round {round}: {string.Join(",", statuses.Select(status => (int)status))}");

            var winner = statuses[0] == HttpStatusCode.NoContent ? candidates[0] : candidates[1];
            var loser = winner == candidates[0] ? candidates[1] : candidates[0];
            await PasswordChangeTests.AssertLoginAsync(client, PasswordResetTests.Email, winner, HttpStatusCode.OK, cancellationToken);
            await PasswordChangeTests.AssertLoginAsync(client, PasswordResetTests.Email, loser, HttpStatusCode.Unauthorized, cancellationToken);
        }
    }
}
