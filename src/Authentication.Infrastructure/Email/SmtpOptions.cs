using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace Authentication.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public SmtpSecurity? Security { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string SenderAddress { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;

    /// <summary>Reads the section leniently; an unparsable port or security mode stays invalid for <see cref="FirstInvalidSetting"/>.</summary>
    public static SmtpOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string Read(string key) => configuration[$"{SectionName}:{key}"] ?? string.Empty;

        return new SmtpOptions
        {
            Host = Read("Host"),
            Port = int.TryParse(Read("Port"), out var port) ? port : 0,
            Security = ParseSecurity(Read("Security")),
            Username = Read("Username"),
            Password = Read("Password"),
            SenderAddress = Read("SenderAddress"),
            SenderName = Read("SenderName")
        };
    }

    /// <summary>Returns the name of the first invalid setting (never its value), or <see langword="null"/> when all are valid.</summary>
    public string? FirstInvalidSetting()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return $"{SectionName}:Host";
        }

        if (Port is < 1 or > 65535)
        {
            return $"{SectionName}:Port";
        }

        if (Security is null)
        {
            return $"{SectionName}:Security";
        }

        // Credentials are used together or not at all.
        if (string.IsNullOrWhiteSpace(Username) != string.IsNullOrWhiteSpace(Password))
        {
            return string.IsNullOrWhiteSpace(Username) ? $"{SectionName}:Username" : $"{SectionName}:Password";
        }

        if (string.IsNullOrWhiteSpace(SenderAddress) || !new EmailAddressAttribute().IsValid(SenderAddress))
        {
            return $"{SectionName}:SenderAddress";
        }

        return string.IsNullOrWhiteSpace(SenderName) ? $"{SectionName}:SenderName" : null;
    }

    // Only the three explicit modes: no automatic or opportunistic mode that could fall back to plaintext,
    // and numeric strings are not accepted as enum values.
    private static SmtpSecurity? ParseSecurity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" => SmtpSecurity.None,
        "starttls" => SmtpSecurity.StartTls,
        "sslonconnect" => SmtpSecurity.SslOnConnect,
        _ => null
    };
}
