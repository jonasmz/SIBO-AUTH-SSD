using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RefreshRotationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task ValidCookieRotatesWithinItsFamilyWithoutRequiringAnAccessToken()
    {
        using var factory = new AuthenticationApiFactory(timeProvider: new ControlledTimeProvider(Now));
        using var client = factory.CreateClient();
        var credential = await LoginAsync(client);

        using var request = AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "expired-or-irrelevant");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["accessToken", "expiresAtUtc"], body.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        using var accessPayload = JsonDocument.Parse(Base64UrlEncoder.Decode(body.RootElement.GetProperty("accessToken").GetString()!.Split('.')[1]));
        Assert.Equal("Administrator", accessPayload.RootElement.GetProperty("role").GetString());
        var replacement = ExtractCookieValue(Assert.Single(response.Headers.GetValues("Set-Cookie")));
        Assert.NotEqual(credential, replacement);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var family = Assert.Single(await db.RenewableSessionFamilies.ToListAsync(TestContext.Current.CancellationToken));
        var credentials = await db.RefreshCredentials.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, credentials.Count);
        Assert.Equal(Now.AddDays(7), family.ExpiresAtUtc);
        Assert.Equal(Now, Assert.Single(credentials, item => item.ConsumedAtUtc is not null).ConsumedAtUtc);
        Assert.Equal(family.ExpiresAtUtc, Assert.Single(credentials, item => item.ConsumedAtUtc is null).ExpiresAtUtc);
    }

    [Fact]
    public async Task PersistenceFailureReturnsGeneric503WithoutReplacementCookie()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        var credential = await LoginAsync(client);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(factory.Resources.DatabasePath + suffix);
        }
        await File.WriteAllTextAsync(factory.Resources.DatabasePath, "not a sqlite database", TestContext.Current.CancellationToken);

        using var response = await RefreshAsync(client, credential);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.DoesNotContain("sqlite", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownMalformedExpiredAndRevokedCredentialsHaveTheSame401WithoutCookie()
    {
        using var factory = new AuthenticationApiFactory(timeProvider: new ControlledTimeProvider(Now));
        using var client = factory.CreateClient();
        using var protector = new RefreshCredentialProtector();
        var unknown = protector.CreateRawCredential();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var expiredFamily = new RenewableSessionFamily("expired-family", DatabaseInitializer.AdministratorUserId, Now.AddDays(-8), Now.AddDays(-1));
            var expiredRaw = protector.CreateRawCredential();
            Assert.True(RefreshCredentialProtector.TryHash(expiredRaw, out var expiredHash));
            var revokedFamily = new RenewableSessionFamily("revoked-family", DatabaseInitializer.AdministratorUserId, Now.AddDays(-2), Now.AddDays(5));
            revokedFamily.Revoke(Now, SessionRevocationReason.Logout);
            var revokedRaw = protector.CreateRawCredential();
            Assert.True(RefreshCredentialProtector.TryHash(revokedRaw, out var revokedHash));
            db.AddRange(expiredFamily, revokedFamily);
            db.RefreshCredentials.AddRange(
                new RefreshCredential("expired-credential", expiredFamily.Id, expiredHash, Now.AddDays(-8), Now.AddDays(-1)),
                new RefreshCredential("revoked-credential", revokedFamily.Id, revokedHash, Now.AddDays(-2), Now.AddDays(5)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var responses = new[]
            {
                await RefreshAsync(client, "malformed"),
                await RefreshAsync(client, unknown),
                await RefreshAsync(client, expiredRaw),
                await RefreshAsync(client, revokedRaw)
            };
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                    Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
                }
            }
        }
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return ExtractCookieValue(Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string credential) =>
        client.SendAsync(AuthenticationApiFactory.CreateBrowserRequest(HttpMethod.Post, "/api/auth/refresh", credential), TestContext.Current.CancellationToken);

    private static string ExtractCookieValue(string cookie) => cookie.Split(';', 2)[0].Split('=', 2)[1];
}
