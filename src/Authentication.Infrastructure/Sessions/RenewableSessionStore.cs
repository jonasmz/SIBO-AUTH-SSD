using Authentication.Application.Features.Sessions;
using Authentication.Application.Features.Login;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;

namespace Authentication.Infrastructure.Sessions;

public sealed partial class RenewableSessionStore(
    AuthenticationDbContext dbContext,
    RefreshCredentialProtector protector,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<RefreshSessionOptions> options,
    UserManager<ApplicationUser> userManager,
    ILogger<RenewableSessionStore> logger) : IRenewableSessionStore, IRefreshSessionRotation
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

    public async Task<RefreshSessionRotationResult> RotateAsync(RefreshSessionRotationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PresentedTokenHash.Length != 32 || command.ReplacementTokenHash.Length != 32 || command.ConsumedAtUtc.Offset != TimeSpan.Zero)
        {
            return RefreshSessionRotationResult.Invalid;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var credential = await dbContext.RefreshCredentials
            .SingleOrDefaultAsync(candidate => candidate.TokenHash.SequenceEqual(command.PresentedTokenHash), cancellationToken);
        if (credential is null)
        {
            return RefreshSessionRotationResult.Invalid;
        }

        var family = await dbContext.RenewableSessionFamilies
            .SingleOrDefaultAsync(candidate => candidate.Id == credential.FamilyId, cancellationToken);
        if (family is null)
        {
            return RefreshSessionRotationResult.Invalid;
        }

        if (credential.ConsumedAtUtc is not null)
        {
            family.Revoke(command.ConsumedAtUtc, SessionRevocationReason.Replay);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogReplay(
                logger,
                family.Id,
                family.UserId,
                command.ConsumedAtUtc,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString());
            return RefreshSessionRotationResult.Invalid;
        }

        if (!family.IsActive(command.ConsumedAtUtc) || credential.RevokedAtUtc is not null || credential.ExpiresAtUtc <= command.ConsumedAtUtc)
        {
            return RefreshSessionRotationResult.Invalid;
        }

        var user = await userManager.FindByIdAsync(family.UserId);
        if (user is null || !user.IsEnabled || (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user)))
        {
            return RefreshSessionRotationResult.Invalid;
        }

        var replacementId = Guid.NewGuid().ToString();
        if (!credential.Consume(command.ConsumedAtUtc, replacementId))
        {
            return RefreshSessionRotationResult.Invalid;
        }

        dbContext.RefreshCredentials.Add(new RefreshCredential(replacementId, family.Id, command.ReplacementTokenHash, command.ConsumedAtUtc, family.ExpiresAtUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var roles = await userManager.GetRolesAsync(user);
        return new RefreshSessionRotationResult(new AuthenticatedIdentity(user.Id, user.Email ?? string.Empty, [.. roles]), family.ExpiresAtUtc);
    }

    [LoggerMessage(LogLevel.Warning, "Refresh credential replay detected for session family {FamilyId}, user {UserId}, at {OccurredAtUtc}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogReplay(ILogger logger, string familyId, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}
