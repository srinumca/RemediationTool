using RemediationTool.Domain.Enum;

namespace RemediationTool.Domain.Entities;

/// <summary>
/// Audit record for a single EDG report upload.
/// Stored in DynamoDB: gfr-edg-reports-dev
/// Primary key: jobId (= ReportUid)
///
/// ReportUid links:
///   S3 folder: gfr-edg-bucket-dev/{yyyy}/{MM}/{reportUid}/
///   Findings:  gfr-edg-findings-dev (jobId on every row)
///   Rejected:  gfr-edg-rejected-dev (jobId on every row)
/// </summary>
public class IngestionJobAudit
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Unique ID for this EDG report upload.
    /// Format: ING-{yyyyMMdd}-{HHmmss}-{random8}
    /// Used as S3 folder name and DynamoDB primary key.
    /// </summary>
    public string ReportUid { get; set; } = string.Empty;

    /// <summary>Same as ReportUid — used as DynamoDB primary key (jobId attribute).</summary>
    public string JobId { get; set; } = string.Empty;

    // ── File metadata ─────────────────────────────────────────────────────────

    public string InboundFileName { get; set; } = string.Empty;

    /// <summary>Size of the uploaded file in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>File extension — csv or xlsx.</summary>
    public string FileFormat { get; set; } = string.Empty;

    // ── S3 links ──────────────────────────────────────────────────────────────

    /// <summary>S3 folder prefix: {yyyy}/{MM}/{reportUid}/</summary>
    public string S3FolderPath { get; set; } = string.Empty;

    /// <summary>Full S3 key of the uploaded CSV/XLSX source file.</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Full S3 key of report-metadata.json (same folder as source file).</summary>
    public string MetadataJsonPath { get; set; } = string.Empty;

    /// <summary>Legacy alias for SourceFilePath.</summary>
    public string? ArchivedFilePath { get; set; }

    /// <summary>Legacy alias for MetadataJsonPath.</summary>
    public string? ProcessingSummaryPath { get; set; }

    // ── Parquet working file (internal — resume flow) ─────────────────────────

    public string? WorkingFilePath { get; set; }
    public string? WorkingFileFormat { get; set; }
    public int WorkingFileRecordCount { get; set; }

    // ── Who / when ────────────────────────────────────────────────────────────

    public string UploadedBy { get; set; } = "System";
    public string UserName { get; set; } = "System";
    public string StartedBy { get; set; } = "System";
    public DateTime StartTimestampUtc { get; set; }
    public DateTime? EndTimestampUtc { get; set; }

    /// <summary>Display name of the uploader (e.g. "Bockoven, Brenna (ES)"). New field — added to match gfr-edg-reports-dev export.</summary>
    public string? UploadedDisplayName { get; set; }

    /// <summary>Email address of the uploader. New field — added to match gfr-edg-reports-dev export.</summary>
    public string? UploadedEmailId { get; set; }

    /// <summary>MD5 checksum of the inbound file, used to detect duplicate uploads. New field — added to match gfr-edg-reports-dev export.</summary>
    public string? InboundFileChecksum { get; set; }

    // ── Status ────────────────────────────────────────────────────────────────

    public IngestionJobStatus Status { get; set; } = IngestionJobStatus.Started;
    public string? ErrorMessage { get; set; }
    public string? FailureReason { get; set; }

    // ── Ingestion counts ──────────────────────────────────────────────────────

    public string? SourceSystem { get; set; }
    public string TriggerType { get; set; } = "Manual";
    public string IngestionMode { get; set; } = "Full";
    public int PayloadRecordCount { get; set; }
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int RejectCount { get; set; }
    public int ValidationFailureCount { get; set; }

    /// <summary>
    /// Count of ingested records by FindingType string key.
    /// Drives the 6 stat cards on the dashboard.
    /// </summary>
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
}