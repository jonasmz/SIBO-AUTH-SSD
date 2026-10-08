using Authentication.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Authentication.Infrastructure.Logging;

/// <summary>
/// Settings of the persistent log files (Technical Constraints §15.3–15.6): the directory that receives the daily
/// UTC files and how many days of files are kept.
/// </summary>
public sealed class PersistentFileLoggerOptions
{
    public const string SectionName = "Logging:File";
    public const int DefaultRetentionDays = 30;

    public string Directory { get; init; } = string.Empty;

    public int RetentionDays { get; init; } = DefaultRetentionDays;

    /// <summary>Reads the section leniently; a blank retention keeps the default and an unparsable one stays invalid.</summary>
    public static PersistentFileLoggerOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var retention = configuration[$"{SectionName}:RetentionDays"];

        return new PersistentFileLoggerOptions
        {
            Directory = configuration[$"{SectionName}:Directory"] ?? string.Empty,
            RetentionDays = string.IsNullOrWhiteSpace(retention)
                ? DefaultRetentionDays
                : int.TryParse(retention, out var days) ? days : 0
        };
    }

    /// <summary>Returns the name of the first invalid setting (never its value), or <see langword="null"/> when all are valid.</summary>
    public string? FirstInvalidSetting()
    {
        // The directory must already exist and be writable: a wrong or missing mount is a deployment error,
        // so it is reported instead of being created silently.
        if (!DataProtectionStorageOptions.IsUsableDirectory(Directory))
        {
            return $"{SectionName}:Directory";
        }

        return RetentionDays < 1 ? $"{SectionName}:RetentionDays" : null;
    }
}
