using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Models;

public class ProcessingSummaryArtifact
{
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public DateTime ProcessingStartTimeUtc { get; set; }

    public DateTime? ProcessingEndTimeUtc { get; set; }

    public int TotalRowsProcessed { get; set; }

    public int SuccessfulRows { get; set; }

    public int FailedRows { get; set; }

    public IngestionJobStatus FinalJobStatus { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ArchivedFilePath { get; set; }

    public List<RejectedRowSummary> RejectedRows { get; set; } = new();
}