using Authentication.Application.Features.Login;

namespace Authentication.Application.Features.Sessions;

public sealed class RefreshSessionHandler(IRefreshSessionRotation rotation, IAccessTokenIssuer issuer, TimeProvider timeProvider)
{
    public async Task<RefreshSessionOutcome> HandleAsync(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await rotation.RotateAsync(
            new RefreshSessionRotationCommand(command.PresentedTokenHash, command.ReplacementTokenHash, timeProvider.GetUtcNow()),
            cancellationToken);

        return result.Identity is null
            ? RefreshSessionOutcome.Invalid
            : new RefreshSessionOutcome(issuer.Issue(result.Identity), result.ExpiresAtUtc);
    }
}
