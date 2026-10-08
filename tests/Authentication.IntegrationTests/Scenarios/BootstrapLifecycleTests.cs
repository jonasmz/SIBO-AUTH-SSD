using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class BootstrapLifecycleTests
{
    private const string ChangedEmail = "changed.admin@example.test";
    private const string ChangedPassword = "Changed-Password-1";

    [Fact]
    public async Task RestartPreservesModifiedAdministratorStateAndSigningMaterial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();
        var keyFingerprint = Fingerprint(resources.PrivateKeyPath);

        using (var first = new AuthenticationApiFactory(sharedResources: resources))
        {
            using var client = first.CreateClient();
            using var scope = first.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<string>>>();
            var admin = await userManager.FindByIdAsync(DatabaseInitializer.AdministratorUserId);
            Assert.NotNull(admin);

            Assert.True((await userManager.SetEmailAsync(admin, ChangedEmail)).Succeeded);
            Assert.True((await userManager.RemovePasswordAsync(admin)).Succeeded);
            Assert.True((await userManager.AddPasswordAsync(admin, ChangedPassword)).Succeeded);
        }

        string subjectAfterRestart;

        using (var second = new AuthenticationApiFactory(sharedResources: resources))
        {
            using var client = second.CreateClient();

            using var oldCredentials = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "admin@local.invalid", password = "admin" },
                cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, oldCredentials.StatusCode);

            using var changedCredentials = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = ChangedEmail, password = ChangedPassword },
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, changedCredentials.StatusCode);
            subjectAfterRestart = DecodeSubject(
                (await changedCredentials.Content.ReadFromJsonAsync<LoginBody>(cancellationToken))!.AccessToken);

            using var scope = second.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<string>>>();

            var role = Assert.Single(await context.Roles.ToListAsync(cancellationToken));
            Assert.Equal(DatabaseInitializer.AdministratorRoleId, role.Id);
            var user = Assert.Single(await context.Users.ToListAsync(cancellationToken));
            Assert.Equal(DatabaseInitializer.AdministratorUserId, user.Id);
            Assert.Equal(ChangedEmail, user.Email);
            Assert.Single(await context.UserRoles.ToListAsync(cancellationToken));
            Assert.Equal(["Administrator"], await userManager.GetRolesAsync(user));
        }

        Assert.Equal(DatabaseInitializer.AdministratorUserId, subjectAfterRestart);
        Assert.Equal(keyFingerprint, Fingerprint(resources.PrivateKeyPath));
    }

    [Fact]
    public async Task RepeatedStartupsNeverDuplicateBuiltInIdentityState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();

        for (var startup = 0; startup < 3; startup++)
        {
            using var factory = new AuthenticationApiFactory(sharedResources: resources);
            using var client = factory.CreateClient();
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();

            Assert.Single(await context.Users.ToListAsync(cancellationToken));
            Assert.Single(await context.Roles.ToListAsync(cancellationToken));
            Assert.Single(await context.UserRoles.ToListAsync(cancellationToken));
        }
    }

    private static string Fingerprint(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string DecodeSubject(string token)
    {
        var payload = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(token.Split('.')[1]);
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("sub").GetString()!;
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);
}
