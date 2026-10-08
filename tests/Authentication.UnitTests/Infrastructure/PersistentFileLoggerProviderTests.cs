using System.Diagnostics;
using System.Text;
using Authentication.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class PersistentFileLoggerProviderTests : IDisposable
{
    private static readonly string Padding = new('x', 200);
    private static readonly DateTimeOffset Day1 = new(2026, 10, 8, 23, 59, 58, 123, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "authentication-api-tests", "logs-" + Guid.NewGuid().ToString("N"));

    public PersistentFileLoggerProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WritesOneUtf8LinePerEventWithTimeLevelCategoryEventIdAndCorrelation()
    {
        var clock = new MutableTimeProvider(Day1);
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();

        using (var provider = Provider(clock))
        {
            var logger = provider.CreateLogger("Authentication.Tests.Category");
            logger.Log(LogLevel.Warning, new EventId(7, "Named"), "first line\r\nsecond line\nthird", null, (state, _) => state);
            logger.Log(LogLevel.Information, 0, "no event id", null, (state, _) => state);
        }

        var bytes = File.ReadAllBytes(Path.Combine(_directory, "auth-2026-10-08.log"));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "the file must not start with a byte-order mark");

        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(
            $"2026-10-08T23:59:58.123Z [Warning] Authentication.Tests.Category[7] trace={activity.TraceId.ToHexString()} span={activity.SpanId.ToHexString()} first line\\nsecond line\\nthird",
            lines[0]);
        Assert.StartsWith("2026-10-08T23:59:58.123Z [Information] Authentication.Tests.Category trace=", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void OmitsCorrelationWithoutAnActivityAndWritesExceptionsWithoutTheirMessage()
    {
        Assert.Null(Activity.Current);
        Exception thrown;
        try
        {
            throw new InvalidOperationException("password=Sup3r-Secret\nsecond line");
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        using (var provider = Provider(new MutableTimeProvider(Day1)))
        {
            provider.CreateLogger("Category").Log(LogLevel.Error, 0, "failed", thrown, (state, _) => state);
        }

        var line = Assert.Single(ReadLines("auth-2026-10-08.log"));
        Assert.StartsWith("2026-10-08T23:59:58.123Z [Error] Category failed exception=System.InvalidOperationException ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("trace=", line, StringComparison.Ordinal);
        Assert.Contains(nameof(OmitsCorrelationWithoutAnActivityAndWritesExceptionsWithoutTheirMessage), line, StringComparison.Ordinal);
        Assert.DoesNotContain("Sup3r-Secret", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchesToTheNextFileWhenTheUtcDateOfAnEventChanges()
    {
        var clock = new MutableTimeProvider(Day1);
        using (var provider = Provider(clock))
        {
            var logger = provider.CreateLogger("Category");
            logger.Log(LogLevel.Information, 0, "on day one", null, (state, _) => state);

            // Two seconds later the UTC date changes and the event goes to the next day's file.
            clock.Advance(TimeSpan.FromSeconds(2));
            logger.Log(LogLevel.Information, 0, "on day two", null, (state, _) => state);
        }

        Assert.Equal(
            ["auth-2026-10-08.log", "auth-2026-10-09.log"],
            Directory.EnumerateFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.EndsWith(" on day one", Assert.Single(ReadLines("auth-2026-10-08.log")), StringComparison.Ordinal);
        Assert.EndsWith(" on day two", Assert.Single(ReadLines("auth-2026-10-09.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletesOnlyLogFilesBeyondTheRetentionAtStartup()
    {
        // On 2026-10-08 with a two-day retention, files dated before 2026-10-06 are expired.
        File.WriteAllText(Path.Combine(_directory, "auth-2026-10-04.log"), "old\n");
        File.WriteAllText(Path.Combine(_directory, "auth-2026-10-05.log"), "old\n");
        File.WriteAllText(Path.Combine(_directory, "auth-2026-10-06.log"), "kept\n");
        File.WriteAllText(Path.Combine(_directory, "auth-latest.log"), "not a dated log\n");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not a log\n");

        using (var provider = Provider(new MutableTimeProvider(Day1), retentionDays: 2))
        {
            provider.CreateLogger("Category").Log(LogLevel.Information, 0, "today", null, (state, _) => state);
        }

        Assert.Equal(
            ["auth-2026-10-06.log", "auth-2026-10-08.log", "auth-latest.log", "notes.txt"],
            Directory.EnumerateFiles(_directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RotationRemovesAFileThatAShortenedRetentionLeavesBehind()
    {
        File.WriteAllText(Path.Combine(_directory, "auth-2026-10-07.log"), "yesterday\n");

        // With a one-day retention the file of 2026-10-07 is still kept on 2026-10-08 ...
        var clock = new MutableTimeProvider(Day1);
        using (var provider = Provider(clock, retentionDays: 1))
        {
            provider.CreateLogger("Category").Log(LogLevel.Information, 0, "today", null, (state, _) => state);
            Assert.True(File.Exists(Path.Combine(_directory, "auth-2026-10-07.log")));

            // ... and removed at the rotation into 2026-10-09.
            clock.Advance(TimeSpan.FromSeconds(2));
            provider.CreateLogger("Category").Log(LogLevel.Information, 0, "tomorrow", null, (state, _) => state);
        }

        Assert.False(File.Exists(Path.Combine(_directory, "auth-2026-10-07.log")));
    }

    [Fact]
    public async Task ConcurrentWritersNeverInterleaveOrLoseLines()
    {
        const int writers = 16;
        const int eventsPerWriter = 500;

        using (var provider = Provider(new MutableTimeProvider(Day1)))
        {
            await Task.WhenAll(Enumerable.Range(0, writers).Select(writer => Task.Run(() =>
            {
                var logger = provider.CreateLogger($"Writer{writer}");
                for (var i = 0; i < eventsPerWriter; i++)
                {
                    logger.Log(LogLevel.Information, 0, (Writer: writer, Event: i), null, (state, _) => $"writer {state.Writer} event {state.Event} {Padding}");
                }
            }, TestContext.Current.CancellationToken)));
        }

        // Disposing drained the queue: every event is present exactly once as a whole line.
        var lines = ReadLines("auth-2026-10-08.log");
        Assert.Equal(writers * eventsPerWriter, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^2026-10-08T23:59:58\.123Z \[Information\] Writer\d+ writer \d+ event \d+ x{200}$", line));
        Assert.Equal(writers * eventsPerWriter, lines.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AFailedWriteDropsOnlyItsBatchAndTheWriterContinues()
    {
        // A directory in place of today's file makes every open of it fail, as an unusable file would.
        Directory.CreateDirectory(Path.Combine(_directory, "auth-2026-10-08.log"));

        var clock = new MutableTimeProvider(Day1);
        using (var provider = Provider(clock))
        {
            var logger = provider.CreateLogger("Category");
            logger.Log(LogLevel.Information, 0, "lost on day one", null, (state, _) => state);

            clock.Advance(TimeSpan.FromSeconds(2));
            logger.Log(LogLevel.Information, 0, "kept on day two", null, (state, _) => state);
        }

        Assert.EndsWith(" kept on day two", Assert.Single(ReadLines("auth-2026-10-09.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresDisabledLevelsAndToleratesASecondDispose()
    {
        var provider = Provider(new MutableTimeProvider(Day1));
        var logger = provider.CreateLogger("Category");

        Assert.False(logger.IsEnabled(LogLevel.None));
        logger.Log(LogLevel.None, 0, "never written", null, (state, _) => state);
        logger.Log(LogLevel.Debug, 0, "written", null, (state, _) => state);
        provider.Dispose();
        provider.Dispose();

        Assert.EndsWith(" written", Assert.Single(ReadLines("auth-2026-10-08.log")), StringComparison.Ordinal);
    }

    private PersistentFileLoggerProvider Provider(TimeProvider clock, int retentionDays = 30) =>
        new(Options.Create(new PersistentFileLoggerOptions { Directory = _directory, RetentionDays = retentionDays }), clock);

    private string[] ReadLines(string fileName) =>
        File.ReadAllText(Path.Combine(_directory, fileName), Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
