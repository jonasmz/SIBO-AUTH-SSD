using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Authentication.Infrastructure.Logging;

/// <summary>
/// Formats each enabled event as one line on the calling thread, where <see cref="Activity.Current"/> still
/// carries the request's trace, and hands it to the provider's queue. It performs no file I/O itself.
/// </summary>
public sealed class PersistentFileLogger(string categoryName, PersistentFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var occurredAtUtc = provider.UtcNow();
        provider.Enqueue(new PersistentLogEntry(
            DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
            Format(occurredAtUtc, logLevel, categoryName, eventId, Activity.Current, formatter(state, exception), exception)));
    }

    /// <summary>
    /// <c>{utc} [{Level}] {Category}[{EventId}] trace={TraceId} span={SpanId} {Message}</c>; absent parts are
    /// omitted. An exception adds its type and stack trace but never its message, which may echo input.
    /// </summary>
    internal static string Format(
        DateTimeOffset occurredAtUtc,
        LogLevel logLevel,
        string category,
        EventId eventId,
        Activity? activity,
        string message,
        Exception? exception)
    {
        var line = new StringBuilder(160)
            .Append(occurredAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(" [").Append(logLevel).Append("] ")
            .Append(category);

        if (eventId.Id != 0)
        {
            line.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        if (activity is not null && activity.TraceId != default)
        {
            line.Append(" trace=").Append(activity.TraceId.ToHexString());
            if (activity.SpanId != default)
            {
                line.Append(" span=").Append(activity.SpanId.ToHexString());
            }
        }

        line.Append(' ').Append(SingleLine(message));

        if (exception is not null)
        {
            line.Append(" exception=").Append(exception.GetType().FullName);
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                line.Append(' ').Append(SingleLine(exception.StackTrace));
            }
        }

        return line.ToString();
    }

    private static string SingleLine(string value) =>
        value.Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
}
