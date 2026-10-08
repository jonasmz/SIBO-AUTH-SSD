using Microsoft.Extensions.Configuration;

namespace Authentication.Api.Security;

/// <summary>
/// Request-limit settings. The default values are a project decision (the baseline sets none),
/// documented in docs/phase-7-operations.md; each can be overridden through configuration.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicy Login { get; init; } = new(10, 60);

    public RateLimitPolicy Refresh { get; init; } = new(30, 60);

    public RateLimitPolicy ForgotPassword { get; init; } = new(5, 900);

    public RateLimitPolicy ResetPassword { get; init; } = new(10, 900);

    public RateLimitPolicy ForgotPasswordAddress { get; init; } = new(3, 3600);

    /// <summary>Reads each policy; a blank or missing value keeps the default, an unparsable one stays invalid.</summary>
    public static RateLimitingOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var defaults = new RateLimitingOptions();

        return new RateLimitingOptions
        {
            Login = Read(configuration, nameof(Login), defaults.Login),
            Refresh = Read(configuration, nameof(Refresh), defaults.Refresh),
            ForgotPassword = Read(configuration, nameof(ForgotPassword), defaults.ForgotPassword),
            ResetPassword = Read(configuration, nameof(ResetPassword), defaults.ResetPassword),
            ForgotPasswordAddress = Read(configuration, nameof(ForgotPasswordAddress), defaults.ForgotPasswordAddress)
        };
    }

    /// <summary>Returns the name of the first invalid setting (never its value), or <see langword="null"/>.</summary>
    public string? FirstInvalidSetting()
    {
        foreach (var (name, policy) in Policies())
        {
            if (policy.PermitLimit <= 0)
            {
                return $"{SectionName}:{name}:PermitLimit";
            }

            if (policy.WindowSeconds <= 0)
            {
                return $"{SectionName}:{name}:WindowSeconds";
            }
        }

        return null;
    }

    private IEnumerable<(string Name, RateLimitPolicy Policy)> Policies()
    {
        yield return (nameof(Login), Login);
        yield return (nameof(Refresh), Refresh);
        yield return (nameof(ForgotPassword), ForgotPassword);
        yield return (nameof(ResetPassword), ResetPassword);
        yield return (nameof(ForgotPasswordAddress), ForgotPasswordAddress);
    }

    private static RateLimitPolicy Read(IConfiguration configuration, string name, RateLimitPolicy fallback) => new(
        ReadInt(configuration[$"{SectionName}:{name}:PermitLimit"], fallback.PermitLimit),
        ReadInt(configuration[$"{SectionName}:{name}:WindowSeconds"], fallback.WindowSeconds));

    private static int ReadInt(string? value, int fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : int.TryParse(value, out var parsed) ? parsed : 0;
}
