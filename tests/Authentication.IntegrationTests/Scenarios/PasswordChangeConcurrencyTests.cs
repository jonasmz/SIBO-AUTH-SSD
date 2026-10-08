using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class PasswordChangeConcurrencyTests
{
    [Fact]
    public async Task TwoSimultaneousChangesLeaveExactlyOneCoherentPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var admin = AdminTestSupport.WithBearer(factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        const string email = "member@example.test";
        _ = await AdminTestSupport.CreateUserAsync(admin, email, "Passw0rd!", cancellationToken: cancellationToken);
        var token = await AdminTestSupport.LoginAsync(anonymous, email, "Passw0rd!", cancellationToken);

        var current = "Passw0rd!";
        for (var round = 0; round < 4; round++)
        {
            string[] candidates = [$"Round-{round}-First!", $"Round-{round}-Second!"];
            var gate = new TaskCompletionSource();
            var attempts = candidates.Select(candidate => Task.Run(async () =>
            {
                await gate.Task;
                using var response = await PasswordChangeTests.SendAsync(
                    anonymous, PasswordChangeTests.Json(current, candidate), token, cancellationToken);
                return response.StatusCode;
            }, cancellationToken)).ToArray();
            gate.SetResult();
            var statuses = await Task.WhenAll(attempts);

            Assert.True(
                statuses.Order().SequenceEqual([HttpStatusCode.NoContent, HttpStatusCode.Unauthorized]),
                $"round {round}: {string.Join(",", statuses.Select(status => (int)status))}");

            // Only the winner's new password authenticates; the loser's was never stored.
            var winner = statuses[0] == HttpStatusCode.NoContent ? candidates[0] : candidates[1];
            var loser = winner == candidates[0] ? candidates[1] : candidates[0];
            await PasswordChangeTests.AssertLoginAsync(anonymous, email, winner, HttpStatusCode.OK, cancellationToken);
            await PasswordChangeTests.AssertLoginAsync(anonymous, email, loser, HttpStatusCode.Unauthorized, cancellationToken);
            current = winner;
        }
    }
}
