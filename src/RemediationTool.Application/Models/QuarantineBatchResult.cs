namespace RemediationTool.Application.Models;

public sealed class QuarantineBatchResult
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public int RequestedCount { get; set; }

    public int QueuedCount { get; set; }

    public int ProcessedCount { get; set; }

    public int SucceededCount { get; set; }

    public int FailedCount { get; set; }

    public int SkippedCount { get; set; }

    public string Message { get; set; } = string.Empty;

    public List<QuarantineItemResult> Items { get; set; } = new();
}
