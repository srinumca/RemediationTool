// FILE: src/RemediationTool.Application/Logging/LogPerformanceScope.cs

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace RemediationTool.Application.Logging;

public sealed class LogPerformanceScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly object? _context;
    private readonly Stopwatch _stopwatch;
    private readonly TimeSpan _slowThreshold;
    private bool _disposed;
    private bool _failed;

    public LogPerformanceScope(
        ILogger logger,
        string operationName,
        object? context = null,
        TimeSpan? slowThreshold = null)
    {
        _logger = logger;
        _operationName = operationName;
        _context = context;
        _slowThreshold = slowThreshold ?? TimeSpan.FromMilliseconds(1000);
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>Call this from inside a catch block to mark the operation as failed
    /// before the scope disposes — changes the final log line to a warning-level
    /// failure entry instead of a normal completion entry.</summary>
    public void MarkFailed() => _failed = true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopwatch.Stop();
        var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;

        if (_failed)
        {
            _logger.LogWarning(
                "[PERF] {Operation} FAILED after {ElapsedMs}ms. Context={@Context}",
                _operationName, elapsedMs, _context);
        }
        else if (_stopwatch.Elapsed > _slowThreshold)
        {
            _logger.LogWarning(
                "[PERF SLOW] {Operation} completed in {ElapsedMs}ms (threshold={ThresholdMs}ms). Context={@Context}",
                _operationName, elapsedMs, _slowThreshold.TotalMilliseconds, _context);
        }
        else
        {
            _logger.LogInformation(
                "[PERF] {Operation} completed in {ElapsedMs}ms. Context={@Context}",
                _operationName, elapsedMs, _context);
        }
    }
}