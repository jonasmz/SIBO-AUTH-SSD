using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Authentication.Infrastructure.Logging;

/// <summary>
/// Minimal first-party file provider (Technical Constraints §15.3–15.6). Loggers enqueue formatted lines on an
/// unbounded in-memory channel, so <c>ILogger.Log</c> never performs file I/O; one background writer appends them,
/// in order, to <c>auth-yyyy-MM-dd.log</c> by UTC date. A single reader means concurrent events can never
/// interleave, and an unbounded queue means none is dropped. Files older than the retention are deleted at
/// startup and at each daily rotation; disposing drains the queue. A failed write drops only its batch from the
/// file; should the writer ever stop, the queue is closed so it cannot grow unbounded.
/// </summary>
[ProviderAlias("File")]
public sealed class PersistentFileLoggerProvider : ILoggerProvider
{
    private const string FilePrefix = "auth-";
    private const string FileExtension = ".log";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Channel<PersistentLogEntry> _queue =
        Channel.CreateUnbounded<PersistentLogEntry>(new UnboundedChannelOptions { SingleReader = true });

    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly TimeProvider _timeProvider;
    private readonly Task _writer;
    private bool _disposed;

    public PersistentFileLoggerProvider(IOptions<PersistentFileLoggerOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        _directory = options.Value.Directory;
        _retentionDays = options.Value.RetentionDays;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _writer = Task.Run(WriteAsync);
    }

    public ILogger CreateLogger(string categoryName) => new PersistentFileLogger(categoryName, this);

    internal DateTimeOffset UtcNow() => _timeProvider.GetUtcNow();

    internal void Enqueue(PersistentLogEntry entry) => _queue.Writer.TryWrite(entry);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();

        // Drain what was already queued; a broken disk must neither hang nor fail shutdown.
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // The writer stopped on an I/O error; the console provider still carries every event.
        }
    }

    private async Task WriteAsync()
    {
        try
        {
            await DrainAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // An unexpected failure ends the writer. Completing the queue makes Enqueue a no-op, so the unbounded
            // channel cannot keep growing for the rest of the process; the console still carries every event.
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out _))
            {
            }

            ReportFailure("stopped", exception);
        }
    }

    private async Task DrainAsync()
    {
        TryDeleteExpiredFiles(DateOnly.FromDateTime(UtcNow().UtcDateTime));

        StreamWriter? file = null;
        DateOnly? fileDate = null;
        var failing = false;
        try
        {
            var reader = _queue.Reader;
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                try
                {
                    while (reader.TryRead(out var entry))
                    {
                        if (fileDate != entry.UtcDate)
                        {
                            if (file is not null)
                            {
                                await file.DisposeAsync().ConfigureAwait(false);
                                file = null;
                                TryDeleteExpiredFiles(entry.UtcDate);
                            }

                            file = Open(entry.UtcDate);
                            fileDate = entry.UtcDate;
                        }

                        await file!.WriteLineAsync(entry.Line).ConfigureAwait(false);
                    }

                    await file!.FlushAsync().ConfigureAwait(false);
                    failing = false;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A transient failure (full disk, a file that cannot be opened) drops the current batch from the
                    // file, which is reopened for the next one, instead of ending the writer.
                    if (!failing)
                    {
                        ReportFailure("dropping events until a write succeeds", exception);
                        failing = true;
                    }

                    file = await CloseQuietlyAsync(file).ConfigureAwait(false);
                    fileDate = null;
                }
            }
        }
        finally
        {
            await CloseQuietlyAsync(file).ConfigureAwait(false);
        }
    }

    private static async Task<StreamWriter?> CloseQuietlyAsync(StreamWriter? file)
    {
        if (file is null)
        {
            return null;
        }

        try
        {
            await file.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The buffered lines could not be flushed; they belong to the batch being dropped.
        }

        return null;
    }

    /// <summary>
    /// Written straight to standard error, never through <c>ILogger</c> (which would feed this provider), and with
    /// the exception type only: its message may carry a path or other configuration value.
    /// </summary>
    private static void ReportFailure(string outcome, Exception exception) =>
        Console.Error.WriteLine($"Persistent log file writer {outcome} after {exception.GetType().FullName}; console logging continues.");

    private StreamWriter Open(DateOnly utcDate)
    {
        var path = Path.Combine(_directory, FileName(utcDate));
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        return new StreamWriter(stream, Utf8WithoutBom) { NewLine = "\n" };
    }

    /// <summary>Deletes <c>auth-yyyy-MM-dd.log</c> files dated before <c>today − retention</c>; never other files.</summary>
    private void TryDeleteExpiredFiles(DateOnly today)
    {
        try
        {
            DeleteExpiredFiles(today);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Retried at the next rotation; retention must not stop the writer.
        }
    }

    private void DeleteExpiredFiles(DateOnly today)
    {
        var oldestKept = today.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileExtension}"))
        {
            var name = Path.GetFileName(path);
            var datePart = name[FilePrefix.Length..^FileExtension.Length];
            if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                date < oldestKept)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Retried at the next rotation; logging must not fail because an old file is busy.
                }
            }
        }
    }

    private static string FileName(DateOnly utcDate) =>
        FilePrefix + utcDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + FileExtension;
}
