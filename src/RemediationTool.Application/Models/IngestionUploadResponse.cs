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

    public string? SourceSystem { get; set; }

    public string TriggerType { get; set; } = "Manual";

    public string IngestionMode { get; set; } = "Full";

    public int PayloadRecordCount { get; set; }

    public int ValidationFailureCount { get; set; }

    public List<RejectedRowSummary> RejectedRows { get; set; } = new();

    public int BatchSize { get; set; }

    public int TotalBatches { get; set; }

    public int PersistedBatchCount { get; set; }

    public int LastSuccessfulBatchNumber { get; set; }

    public int LastProcessedRecordCount { get; set; }

    public bool CheckpointingEnabled { get; set; }

    public int BatchPersistenceRetryCount { get; set; }

    public int MaxBatchPersistenceRetryCount { get; set; }

    public bool IsResumeEligible { get; set; }

    public DateTime? LastCheckpointUtc { get; set; }

    public string? CheckpointMessage { get; set; }

    public string? WorkingFileFormat { get; set; }

    public string? WorkingFilePath { get; set; }

    public int WorkingFileRecordCount { get; set; }
    // Count of successfully ingested records grouped by FindingType (Req 7 audit report).
    public Dictionary<string, int> FindingTypeCounts { get; set; } = new();
}