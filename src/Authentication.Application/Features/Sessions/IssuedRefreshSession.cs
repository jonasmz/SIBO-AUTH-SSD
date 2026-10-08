namespace Authentication.Application.Features.Sessions;

public sealed record IssuedRefreshSession(string RawCredential, DateTimeOffset ExpiresAtUtc);
