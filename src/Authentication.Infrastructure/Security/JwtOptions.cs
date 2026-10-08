namespace Authentication.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    public string PrivateKeyPath { get; init; } = string.Empty;

    /// <summary>Expiry clock tolerance in seconds (0-60); supplied by deployment, no default in code.</summary>
    public int ClockSkewSeconds { get; init; } = -1;
}
