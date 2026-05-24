using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Models;

public class ProcessingSummaryArtifact
{
    public string JobId { get; set; } = string.Empty;

    public string? InboundFileName { get; set; }

    public string? SourceSystem { get; set; }

    public string TriggerType { get; set; } = "Manual";

    public string IngestionMode { get; set; } = "Full";

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime ProcessingStartTimeUtc { get; set; }

    public DateTime? ProcessingEndTimeUtc { get; set; }

    public int PayloadRecordCount { get; set; }

    public int TotalRowsProcessed { get; set; }

    public int SuccessfulRows { get; set; }

    public int FailedRows { get; set; }

    public int ValidationFailureCount { get; set; }

    public IngestionJobStatus FinalJobStatus { get; set; }

    public string? Message { get; set; }

    public string? ArchivedFilePath { get; set; }

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
}