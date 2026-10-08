using Authentication.Application.Features.Login;

namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionRotationResult(AuthenticatedIdentity? Identity, DateTimeOffset? ExpiresAtUtc)
{
    public static RefreshSessionRotationResult Invalid { get; } = new(null, null);
}
