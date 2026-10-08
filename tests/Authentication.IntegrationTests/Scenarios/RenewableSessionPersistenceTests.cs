using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken), migration => migration.EndsWith("_AddRenewableSessions", StringComparison.Ordinal));
            var family = new RenewableSessionFamily("family-persistence", DatabaseInitializer.AdministratorUserId, now, now.AddDays(7));
            family.Revoke(now, SessionRevocationReason.Logout);
            db.RenewableSessionFamilies.Add(family);
            db.RefreshCredentials.Add(new RefreshCredential("credential-persistence", family.Id, hash, now, now.AddDays(7)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.RefreshCredentials.Add(new RefreshCredential("credential-duplicate", family.Id, hash, now, now.AddDays(7)));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
            db.ChangeTracker.Clear();

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, UserId, CreatedAtUtc, ExpiresAtUtc, COALESCE(RevokedAtUtc, ''), COALESCE(CAST(RevocationReason AS TEXT), ''), '', ''
                FROM RenewableSessionFamilies
                UNION ALL
                SELECT Id, FamilyId, hex(TokenHash), CreatedAtUtc, ExpiresAtUtc, COALESCE(ConsumedAtUtc, ''), COALESCE(RevokedAtUtc, ''), COALESCE(ReplacedByTokenId, '')
                FROM RefreshCredentials
                """;
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    Assert.DoesNotContain(raw, reader.GetValue(index)?.ToString() ?? string.Empty, StringComparison.Ordinal);
                }
            }
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
