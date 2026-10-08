namespace Authentication.IntegrationTests.Infrastructure;

public sealed record LoginTokenBody(string AccessToken, DateTime ExpiresAtUtc);
