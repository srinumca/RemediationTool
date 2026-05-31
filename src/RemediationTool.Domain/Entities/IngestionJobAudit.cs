using RemediationTool.Domain.Enum;

namespace RemediationTool.Domain.Entities;

public class IngestionJobAudit
{
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public string UserName { get; set; } = "System";

    public string StartedBy { get; set; } = "System";

    public DateTime StartTimestampUtc { get; set; }

    public DateTime? EndTimestampUtc { get; set; }

    public string? SourceSystem { get; set; }

    public string TriggerType { get; set; } = "Manual";

    public string IngestionMode { get; set; } = "Full";

    public int PayloadRecordCount { get; set; }

    public int TotalRecords { get; set; }

    public int SuccessCount { get; set; }

    public int RejectCount { get; set; }

    public int ValidationFailureCount { get; set; }

    // --- Finding Type breakdown (Req 7) ---
    // Count of successfully ingested records grouped by FindingType.
    // Satisfies the "Ingestion Success" audit report requirement:
    // "Finding Type, Record Count" per type in the payload.
    // Key = FindingType string value (e.g. "Obsolete", "Quarantined").
    // Value = count of valid records with that type in this job.
    public Dictionary<string, int> FindingTypeCounts { get; set; } = new();

    // Batch / checkpoint metadata
    public int BatchSize { get; set; }

    public int TotalBatches { get; set; }

    public int PersistedBatchCount { get; set; }

    public int LastSuccessfulBatchNumber { get; set; }

    public int LastProcessedRecordCount { get; set; }

    public bool CheckpointingEnabled { get; set; }

    public int BatchPersistenceRetryCount { get; set; }

    public int MaxBatchPersistenceRetryCount { get; set; }

    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Started;

    public string? ErrorMessage { get; set; }

    public string? FailureReason { get; set; }

    public string? ArchivedFilePath { get; set; }

    public string? ProcessingSummaryPath { get; set; }

    public bool IsResumeEligible { get; set; }

    public DateTime? LastCheckpointUtc { get; set; }

    public string? CheckpointMessage { get; set; }

    public string? WorkingFileFormat { get; set; }

    public string? WorkingFilePath { get; set; }

    public int WorkingFileRecordCount { get; set; }
}