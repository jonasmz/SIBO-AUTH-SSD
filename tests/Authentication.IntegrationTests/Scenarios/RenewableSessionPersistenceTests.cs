using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RenewableSessionPersistenceTests
{
    [Fact]
    public async Task StoresOnlyDigestAndPreservesFamilyRevocationAcrossFactoryRecreation()
    {
        using var resources = new Phase1TestResources();
        var protector = new RefreshCredentialProtector();
        var raw = protector.CreateRawCredential();
        Assert.True(RefreshCredentialProtector.TryHash(raw, out var hash));
        var now = DateTimeOffset.UtcNow;

        await using (var factory = new AuthenticationApiFactory(sharedResources: resources))
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var family = new RenewableSessionFamily("family-persistence", "user-persistence", now, now.AddDays(7));
            family.Revoke(now, SessionRevocationReason.Logout);
            db.RenewableSessionFamilies.Add(family);
            db.RefreshCredentials.Add(new RefreshCredential("credential-persistence", family.Id, hash, now, now.AddDays(7)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reopened = new AuthenticationApiFactory(sharedResources: resources);
        using var reopenedScope = reopened.Services.CreateScope();
        var reopenedDb = reopenedScope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var familyAfterRestart = await reopenedDb.RenewableSessionFamilies.FindAsync(["family-persistence"], TestContext.Current.CancellationToken);
        var credential = await reopenedDb.RefreshCredentials.FindAsync(["credential-persistence"], TestContext.Current.CancellationToken);
        Assert.NotNull(familyAfterRestart);
        Assert.NotNull(familyAfterRestart!.RevokedAtUtc);
        Assert.NotNull(credential);
        Assert.Equal(32, credential!.TokenHash.Length);
        Assert.NotEqual(raw, Convert.ToBase64String(credential.TokenHash));
    }
}
