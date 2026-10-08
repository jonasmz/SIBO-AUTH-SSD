using Authentication.Domain.Sessions;

namespace Authentication.Application.Features.Sessions;

public sealed class LogoutSessionHandler(IRefreshSessionLookup lookup, ISessionFamilyRevocation revocation, TimeProvider timeProvider)
{
    public async Task<LogoutSessionOutcome> HandleAsync(LogoutSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PresentedTokenHash is not { Length: 32 }) return LogoutSessionOutcome.Completed;

        var session = await lookup.FindAsync(command.PresentedTokenHash, cancellationToken);
        if (session is null || session.IsRevoked || session.ExpiresAtUtc <= timeProvider.GetUtcNow()) return LogoutSessionOutcome.Completed;

        await revocation.RevokeFamilyAsync(session.FamilyId, timeProvider.GetUtcNow(), SessionRevocationReason.Logout, cancellationToken);
        return LogoutSessionOutcome.Completed;
    }
}
