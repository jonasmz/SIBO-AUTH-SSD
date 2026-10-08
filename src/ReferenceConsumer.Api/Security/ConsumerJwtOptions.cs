namespace ReferenceConsumer.Api.Security;

public sealed class ConsumerJwtOptions
{
    public const string ServiceNameSetting = "Service:Name";
    public const string IssuerSetting = "Jwt:Issuer";
    public const string AudienceSetting = "Jwt:Audience";
    public const string PublicKeyPathSetting = "Jwt:PublicKeyPath";
    public const string ClockSkewSecondsSetting = "Jwt:ClockSkewSeconds";

    private const int MaxClockSkewSeconds = 60;

    public required string ServiceName { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string PublicKeyPath { get; init; }

    public required TimeSpan ClockSkew { get; init; }

    /// <summary>
    /// Reads and validates the consumer settings. A missing or invalid value fails startup with a
    /// message that names only the setting, never its value.
    /// </summary>
    public static ConsumerJwtOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clockSkewText = configuration[ClockSkewSecondsSetting];
        if (!int.TryParse(clockSkewText, out var clockSkewSeconds) ||
            clockSkewSeconds is < 0 or > MaxClockSkewSeconds)
        {
            throw Invalid(ClockSkewSecondsSetting);
        }

        return new ConsumerJwtOptions
        {
            ServiceName = Required(configuration, ServiceNameSetting),
            Issuer = Required(configuration, IssuerSetting),
            Audience = Required(configuration, AudienceSetting),
            PublicKeyPath = Required(configuration, PublicKeyPathSetting),
            ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds)
        };
    }

    private static string Required(IConfiguration configuration, string setting)
    {
        var value = configuration[setting];

        return string.IsNullOrWhiteSpace(value) ? throw Invalid(setting) : value;
    }

    private static InvalidOperationException Invalid(string setting) =>
        new($"Required configuration '{setting}' is missing or invalid.");
}
