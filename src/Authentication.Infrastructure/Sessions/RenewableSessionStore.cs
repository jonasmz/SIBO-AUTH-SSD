using Authentication.Application.Features.Sessions;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;

namespace Authentication.Infrastructure.Sessions;

public sealed class RenewableSessionStore(AuthenticationDbContext dbContext, RefreshCredentialProtector protector, TimeProvider timeProvider, Microsoft.Extensions.Options.IOptions<RefreshSessionOptions> options) : IRenewableSessionStore
{
    public async Task<IssuedRefreshSession> IssueAsync(string userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddDays(options.Value.LifetimeDays);
        var family = new RenewableSessionFamily(Guid.NewGuid().ToString(), userId, now, expires);
        var raw = protector.CreateRawCredential();
        if (!RefreshCredentialProtector.TryHash(raw, out var hash)) throw new InvalidOperationException();
        dbContext.RenewableSessionFamilies.Add(family);
        dbContext.RefreshCredentials.Add(new RefreshCredential(Guid.NewGuid().ToString(), family.Id, hash, now, expires));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IssuedRefreshSession(raw, expires);
    }
}
