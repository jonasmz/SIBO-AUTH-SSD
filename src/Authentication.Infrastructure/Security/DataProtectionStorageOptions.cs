namespace Authentication.Infrastructure.Security;

public sealed class DataProtectionStorageOptions
{
    public const string SectionName = "DataProtection";

    public string KeysPath { get; init; } = string.Empty;

    /// <summary>True when the directory exists and the process can create and delete a file in it.</summary>
    public static bool IsUsableDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
