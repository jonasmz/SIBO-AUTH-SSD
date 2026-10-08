using Authentication.Application.Features.Login;

namespace Authentication.Application.Features.Sessions;

public sealed record RefreshSessionOutcome(AccessToken? AccessToken, DateTimeOffset? RefreshExpiresAtUtc)
{
    public bool Succeeded => AccessToken is not null;

    public static RefreshSessionOutcome Invalid { get; } = new(null, null);
}
