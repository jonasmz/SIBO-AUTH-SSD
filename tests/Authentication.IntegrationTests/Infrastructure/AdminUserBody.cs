namespace Authentication.IntegrationTests.Infrastructure;

public sealed record AdminUserBody(
    string Id,
    string Email,
    bool Enabled,
    bool IsLockedOut,
    DateTime? LockoutEndUtc,
    string[] Roles);
