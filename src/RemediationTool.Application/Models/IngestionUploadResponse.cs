using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Models;

public class IngestionUploadResponse
{
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public IngestionJobStatus Status { get; set; }

    public int TotalRecords { get; set; }

    public int SuccessCount { get; set; }

    public int RejectCount { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ArchivedFilePath { get; set; }

    public string? ProcessingSummaryPath { get; set; }

    public string Message { get; set; } = string.Empty;

    public List<RejectedRowSummary> RejectedRows { get; set; } = new();
}