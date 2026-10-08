namespace Authentication.Api.Security;

/// <summary>One fixed-window allowance: at most <see cref="PermitLimit"/> requests per <see cref="WindowSeconds"/>.</summary>
public sealed record RateLimitPolicy(int PermitLimit, int WindowSeconds)
{
    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
