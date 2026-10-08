namespace Authentication.Application.Features.Login;

public sealed record LoginOutcome(AccessToken? AccessToken)
{
    public bool Succeeded => AccessToken is not null;

    public static LoginOutcome Success(AccessToken accessToken) => new(accessToken);

    public static LoginOutcome InvalidCredentials { get; } = new((AccessToken?)null);
}
