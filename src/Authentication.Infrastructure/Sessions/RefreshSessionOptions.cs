namespace Authentication.Infrastructure.Sessions;

public sealed class RefreshSessionOptions
{
    public const string SectionName = "RefreshSession";
    public int LifetimeDays { get; init; }
    public string FrontendOrigin { get; init; } = string.Empty;

    public static bool HasValidLifetime(int lifetimeDays)
    {
        if (lifetimeDays <= 0) return false;
        try
        {
            _ = DateTimeOffset.UtcNow.AddDays(lifetimeDays);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public static bool HasValidFrontendOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
