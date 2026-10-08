using System.Data;
using System.Diagnostics;
using Authentication.Application.Features.Passwords;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Authentication.Infrastructure.Identity;

public sealed partial class PasswordChange(
    AuthenticationDbContext context,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<PasswordChange> logger) : IPasswordChange
{
    public async Task<ChangePasswordOutcome> ChangeAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One transaction: SQLite takes its write lock first, and a refusal leaves neither a new
        // credential nor a revoked session behind (disposal without commit rolls everything back).
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
        {
            return ChangePasswordOutcome.InvalidCurrentPassword;
        }

        var now = timeProvider.GetUtcNow();
        var keptFamilyId = await ResolveKeptFamilyAsync(command, now, cancellationToken);

        var changed = await userManager.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!changed.Succeeded)
        {
            var outcome = Map(changed);
            if (outcome == ChangePasswordOutcome.InvalidCurrentPassword && userManager.SupportsUserLockout)
            {
                // An incorrect current password is a failed password attempt like a failed login
                // (SRS NFR-SEC-BF-001): Identity counts it and applies its configured lockout. Only
                // that counter is committed; the credential and every session stay unchanged.
                await userManager.AccessFailedAsync(user);
                await transaction.CommitAsync(cancellationToken);
            }

            return outcome;
        }

        var families = await context.RenewableSessionFamilies
            .Where(family => family.UserId == user.Id && family.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var toRevoke = families.Where(family => family.IsActive(now) && family.Id != keptFamilyId).ToList();
        foreach (var family in toRevoke)
        {
            family.Revoke(now, SessionRevocationReason.PasswordChanged);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogPasswordChanged(
            logger,
            user.Id,
            toRevoke.Count,
            keptFamilyId is not null,
            now,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString());

        return ChangePasswordOutcome.Changed;
    }

    /// <summary>
    /// The family identified by the presented refresh credential is kept only while that credential
    /// could itself still refresh and belongs to the same user; otherwise nothing is kept.
    /// </summary>
    private async Task<string?> ResolveKeptFamilyAsync(ChangePasswordCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (command.PresentedRefreshTokenHash is not { Length: 32 } hash)
        {
            return null;
        }

        var credential = await context.RefreshCredentials.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash.SequenceEqual(hash), cancellationToken);
        if (credential is null || credential.ConsumedAtUtc is not null || credential.RevokedAtUtc is not null || credential.ExpiresAtUtc <= now)
        {
            return null;
        }

        var family = await context.RenewableSessionFamilies.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == credential.FamilyId, cancellationToken);

        return family is not null && family.UserId == command.UserId && family.IsActive(now) ? family.Id : null;
    }

    /// <summary>Maps Identity error codes to a fixed outcome; Identity descriptions never leave this method.</summary>
    private static ChangePasswordOutcome Map(IdentityResult result)
    {
        var codes = result.Errors.Select(error => error.Code).ToList();

        if (codes.Contains("PasswordMismatch"))
        {
            return ChangePasswordOutcome.InvalidCurrentPassword;
        }

        return codes.Exists(code => code.StartsWith("Password", StringComparison.Ordinal))
            ? ChangePasswordOutcome.InvalidNewPassword
            : ChangePasswordOutcome.Invalid;
    }

    [LoggerMessage(LogLevel.Information, "Password changed for user {UserId}; {RevokedCount} other renewable session families revoked, current session kept: {SessionKept}, at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogPasswordChanged(ILogger logger, string userId, int revokedCount, bool sessionKept, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}
