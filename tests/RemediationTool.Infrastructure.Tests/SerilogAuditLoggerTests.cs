using Microsoft.Extensions.Logging;
using RemediationTool.Infrastructure.Logging;
using Xunit;

namespace RemediationTool.Infrastructure.Tests;

public sealed class SerilogAuditLoggerTests
{
    [Fact]
    public void RecordEvent_CreatesAuditScopeAndInformationLog()
    {
        var logger = new RecordingLogger<SerilogAuditLogger>();
        var auditLogger = new SerilogAuditLogger(logger);
        var details = new { Count = 5 };

        auditLogger.RecordEvent(
            "IngestionCompleted",
            "job-1",
            "user@example.com",
            "Success",
            details);

        var scope = Assert.Single(logger.Scopes);
        Assert.Equal("Audit", scope.Values["LogCategory"]);
        Assert.Equal("IngestionCompleted", scope.Values["AuditEventType"]);
        Assert.Equal("job-1", scope.Values["AuditEntityId"]);
        Assert.Equal("user@example.com", scope.Values["AuditActor"]);
        Assert.Equal("Success", scope.Values["AuditOutcome"]);
        Assert.True(scope.Disposed);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("[AUDIT]", entry.Message, StringComparison.Ordinal);
        Assert.Contains("IngestionCompleted", entry.Message, StringComparison.Ordinal);
        Assert.Contains("job-1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordEvent_AllowsNullDetails()
    {
        var logger = new RecordingLogger<SerilogAuditLogger>();
        var auditLogger = new SerilogAuditLogger(logger);

        auditLogger.RecordEvent("Event", "entity", "system", "Failed");

        Assert.Single(logger.Entries);
        Assert.Single(logger.Scopes);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<RecordingScope> Scopes { get; } = new();

        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            var dictionary = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(state)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var scope = new RecordingScope(dictionary);
            Scopes.Add(scope);
            return scope;
        }

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

    private sealed class RecordingScope : IDisposable
    {
        public RecordingScope(IReadOnlyDictionary<string, object> values)
        {
            Values = values;
        }

        public IReadOnlyDictionary<string, object> Values { get; }

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
