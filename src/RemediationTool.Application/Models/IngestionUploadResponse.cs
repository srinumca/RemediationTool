using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Models;

/// <summary>
/// Response returned after a file upload and ingestion.
/// ReportUid links S3 files to DynamoDB records to all finding rows.
/// </summary>
public class IngestionUploadResponse
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Unique ID for this EDG report upload.
    /// Format: ING-{yyyyMMdd}-{HHmmss}-{random8}
    /// Used as S3 folder name and DynamoDB primary key.
    /// </summary>
    public string ReportUid { get; set; } = string.Empty;

    /// <summary>Same as ReportUid — kept for backward compatibility.</summary>
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    // ── S3 paths ──────────────────────────────────────────────────────────────

    /// <summary>S3 folder prefix: {yyyy}/{MM}/{reportUid}/</summary>
    public string S3FolderPath { get; set; } = string.Empty;

    /// <summary>Full S3 key of the uploaded source file.</summary>
    public string? SourceFilePath { get; set; }

    /// <summary>Full S3 key of report-metadata.json (same folder as source).</summary>
    public string? MetadataJsonPath { get; set; }

    /// <summary>Legacy alias for SourceFilePath.</summary>
    public string? ArchivedFilePath { get; set; }

    /// <summary>Legacy alias for MetadataJsonPath.</summary>
    public string? ProcessingSummaryPath { get; set; }

    // ── Status + counts ───────────────────────────────────────────────────────

    public IngestionJobStatus Status { get; set; }

    public int TotalRecords { get; set; }

    public int SuccessCount { get; set; }

    private int _rejectCount;

    /// <summary>
    /// Number of rejected records.
    ///
    /// IngestionService counts invalid mapped findings, while parser-level malformed CSV rows
    /// are recorded only in RejectedRows. Use the larger distinct rejected-row count so
    /// malformed CSV rows are not missed in the response/audit counts.
    /// </summary>
    public int RejectCount
    {
        get
        {
            var rejectedRowCount = RejectedRows
                .Where(x => x.RowNumber > 0)
                .Select(x => x.RowNumber)
                .Distinct()
                .Count();

            return Math.Max(_rejectCount, rejectedRowCount);
        }
        set => _rejectCount = value;
    }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? SourceSystem { get; set; }

    public string TriggerType { get; set; } = "Manual";

    public string IngestionMode { get; set; } = "Full";

    public int PayloadRecordCount { get; set; }

    public int ValidationFailureCount { get; set; }

    public List<RejectedRowSummary> RejectedRows { get; set; } = new();

    // ── Finding type breakdown — drives dashboard stat cards ──────────────────

    public Dictionary<string, int> FindingTypeCounts { get; set; } = new();

    // ── Batch / checkpoint ────────────────────────────────────────────────────

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

    // ── Working file (Parquet — internal resume use) ──────────────────────────

    public string? WorkingFileFormat { get; set; }

    public string? WorkingFilePath { get; set; }

    public int WorkingFileRecordCount { get; set; }
}
