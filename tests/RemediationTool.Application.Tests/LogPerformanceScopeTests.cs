using Microsoft.Extensions.Logging;
using RemediationTool.Application.Logging;
using Xunit;

namespace RemediationTool.Application.Tests;

public sealed class LogPerformanceScopeTests
{
    [Fact]
    public void Dispose_FastSuccessfulOperation_LogsInformation()
    {
        var logger = new RecordingLogger();

        using (new LogPerformanceScope(
                   logger,
                   "FastOperation",
                   new { JobId = "job-1" },
                   TimeSpan.FromMinutes(1)))
        {
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("FastOperation", entry.Message, StringComparison.Ordinal);
        Assert.Contains("completed", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_OperationAboveThreshold_LogsSlowWarning()
    {
        var logger = new RecordingLogger();

        using (new LogPerformanceScope(
                   logger,
                   "SlowOperation",
                   slowThreshold: TimeSpan.Zero))
        {
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("PERF SLOW", entry.Message, StringComparison.Ordinal);
        Assert.Contains("SlowOperation", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkFailed_DisposeLogsFailureWarningInsteadOfSlowOrSuccess()
    {
        var logger = new RecordingLogger();
        var scope = new LogPerformanceScope(
            logger,
            "FailedOperation",
            slowThreshold: TimeSpan.Zero);

        scope.MarkFailed();
        scope.Dispose();

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("FAILED", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PERF SLOW", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_CalledMoreThanOnce_LogsExactlyOnce()
    {
        var logger = new RecordingLogger();
        var scope = new LogPerformanceScope(logger, "Once");

        scope.Dispose();
        scope.Dispose();

        Assert.Single(logger.Entries);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
