namespace Authentication.Infrastructure.Sessions;

public sealed class RefreshSessionOptions
{
    public const string SectionName = "RefreshSession";
    public int LifetimeDays { get; init; }
    public string FrontendOrigin { get; init; } = string.Empty;
}
