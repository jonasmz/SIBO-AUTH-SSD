namespace Authentication.Infrastructure.Logging;

/// <summary>One formatted log line and the UTC date that selects its daily file.</summary>
internal readonly record struct PersistentLogEntry(DateOnly UtcDate, string Line);
