namespace Authentication.Infrastructure.Persistence;

public sealed class SqliteOptions
{
    public const string SectionName = "Persistence";

    public string ConnectionString { get; init; } = string.Empty;
}
