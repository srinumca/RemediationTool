using RemediationTool.Domain.Enum;

namespace RemediationTool.Domain.Entities;

public class IngestionJobAudit
{
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public string UserName { get; set; } = "System";

    public DateTime StartTimestampUtc { get; set; }

    public DateTime? EndTimestampUtc { get; set; }

    public int TotalRecords { get; set; }

    public int SuccessCount { get; set; }

    public int RejectCount { get; set; }

    public IngestionJobStatus Status { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ProcessingSummaryPath { get; set; }

    public string? ArchivedFilePath { get; set; }
}