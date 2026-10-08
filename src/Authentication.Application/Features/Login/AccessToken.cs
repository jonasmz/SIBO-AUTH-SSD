namespace Authentication.Application.Features.Login;

public sealed record AccessToken(string EncodedToken, DateTimeOffset ExpiresAtUtc);
