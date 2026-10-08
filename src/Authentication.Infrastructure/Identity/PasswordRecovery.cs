using System.Data;
using System.Diagnostics;
using System.Text;
using Authentication.Application.Features.PasswordRecovery;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Authentication.Infrastructure.Identity;

public sealed partial class PasswordRecovery(
    AuthenticationDbContext context,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<PasswordRecovery> logger) : IPasswordRecovery
{
    public async Task<PasswordResetTicket?> IssueResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // Identity's own normalization, as used by sign-in and user administration.
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsEnabled)
        {
            return null;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        LogResetRequested(logger, user.Id, timeProvider.GetUtcNow(), Activity.Current?.TraceId.ToString(), Activity.Current?.SpanId.ToString());

        // Base64url keeps the token a single copy-safe word; mail clients may wrap or alter '+', '/' and '='.
        return new PasswordResetTicket(user.Email ?? email, WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)));
    }

    public async Task<ResetPasswordOutcome> ResetAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryDecode(command.Token, out var identityToken))
        {
            return ResetPasswordOutcome.InvalidToken;
        }

        // One transaction: SQLite takes its write lock first, so two resets with one token run one
        // after the other and the second sees the rotated security stamp. A refusal leaves no change.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null || !user.IsEnabled)
        {
            return ResetPasswordOutcome.InvalidToken;
        }

        var reset = await userManager.ResetPasswordAsync(user, identityToken, command.NewPassword);
        if (!reset.Succeeded)
        {
            return Map(reset);
        }

        // A reset ends every renewable session; no session is kept. A failure here rolls back the new
        // password too, so success is never reported while a session could still renew.
        var now = timeProvider.GetUtcNow();
        var revoked = await SessionFamilyRevocation.RevokeActiveAsync(
            context, user.Id, now, SessionRevocationReason.PasswordReset, keepFamilyId: null, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        LogPasswordReset(logger, user.Id, revoked, now, Activity.Current?.TraceId.ToString(), Activity.Current?.SpanId.ToString());

        return ResetPasswordOutcome.Reset;
    }

    private static bool TryDecode(string token, out string identityToken)
    {
        identityToken = string.Empty;
        try
        {
            identityToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

            return identityToken.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Maps Identity error codes to a fixed outcome; Identity descriptions never leave this method.</summary>
    private static ResetPasswordOutcome Map(IdentityResult result)
    {
        var codes = result.Errors.Select(error => error.Code).ToList();

        if (codes.Contains("InvalidToken"))
        {
            return ResetPasswordOutcome.InvalidToken;
        }

        return codes.Exists(code => code.StartsWith("Password", StringComparison.Ordinal))
            ? ResetPasswordOutcome.InvalidNewPassword
            : ResetPasswordOutcome.Invalid;
    }

    [LoggerMessage(LogLevel.Information, "PasswordResetRequested: reset token issued for user {UserId} at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogResetRequested(ILogger logger, string userId, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    [LoggerMessage(LogLevel.Information, "PasswordReset: password reset for user {UserId}; {RevokedCount} renewable session families revoked at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogPasswordReset(ILogger logger, string userId, int revokedCount, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);
}
