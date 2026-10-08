namespace Authentication.Api.Features.Login;

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc);
