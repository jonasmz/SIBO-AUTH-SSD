namespace Authentication.Application.Features.Login;

public sealed record LoginOutcome(AccessToken? AccessToken, string? RefreshCredential = null, DateTimeOffset? RefreshExpiresAtUtc = null)
{
    public bool Succeeded => AccessToken is not null;

    public static LoginOutcome Success(AccessToken accessToken, string refreshCredential, DateTimeOffset refreshExpiresAtUtc) => new(accessToken, refreshCredential, refreshExpiresAtUtc);

    public static LoginOutcome InvalidCredentials { get; } = new((AccessToken?)null);
}
