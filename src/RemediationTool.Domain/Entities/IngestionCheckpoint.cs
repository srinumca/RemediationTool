using RemediationTool.Domain.Enum;

namespace RemediationTool.Domain.Entities;

public class IngestionCheckpoint
{
    private bool _isResumeEligible;

    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public string UserName { get; set; } = "System";

    public string? SourceSystem { get; set; }

    public string TriggerType { get; set; } = "Manual";

    public string IngestionMode { get; set; } = "Full";

    public int BatchSize { get; set; }

    public int TotalBatches { get; set; }

    public int LastSuccessfulBatchNumber { get; set; }

    public int LastProcessedRecordCount { get; set; }

    public int PersistedBatchCount { get; set; }

    public int SuccessCount { get; set; }

    public int RejectCount { get; set; }

    public int BatchPersistenceRetryCount { get; set; }

    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Started;

    /// <summary>
    /// Resume is allowed whenever the job failed after at least one valid record was identified
    /// and the number of records persisted is still less than the valid-record count.
    /// This supports the existing multi-batch resume flow and also allows single-batch
    /// retry when batch 1 fails before it can be marked successful.
    /// </summary>
    public bool IsResumeEligible
    {
        get => Status == IngestionJobStatus.Failed
               && SuccessCount > 0
               && LastProcessedRecordCount < SuccessCount
            || _isResumeEligible;
        set => _isResumeEligible = value;
    }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastCheckpointUtc { get; set; } = DateTime.UtcNow;

    public string? FailureReason { get; set; }

    // --- Working file (Parquet) ---
    // Populated after WriteAsync completes. Used by resume to load records
    // from Parquet instead of JSON staging when a working file exists.
    public string? WorkingFilePath { get; set; }

    public string? WorkingFileFormat { get; set; }

    public int WorkingFileRecordCount { get; set; }
}
