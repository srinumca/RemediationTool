using RemediationTool.Domain;

namespace RemediationTool.Domain.Entities;

/// <summary>
/// Core domain entity representing a single file finding.
/// FindingType is a plain string. Status tracks GFR workflow stage.
/// </summary>
public class FileFinding
{
    // ── System-generated ─────────────────────────────────────────────────────
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RecordVersionId { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceRecordId { get; set; }
    public string? IngestionJobId { get; set; }
    public string InboundFileName { get; set; } = string.Empty;
    public string UserName { get; set; } = "System";
    public DateTime LoadDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdateDateUtc { get; set; } = DateTime.UtcNow;

    // ── Inbound file fields ───────────────────────────────────────────────────
    public string FindingFileName { get; set; } = string.Empty;
    public string FindingFileFormat { get; set; } = string.Empty;
    public long? FindingFileSizeBytes { get; set; }
    public string CurrentFileLocation { get; set; } = string.Empty;

    /// <summary>
    /// EDG finding type — plain string.
    /// Obsolete | Quarantined | Restoration | TotalPendingQuarantined |
    /// Exception | Error | Deleted | Restored | NotObsolete | Exclusion
    /// </summary>
    public string FindingType { get; set; } = string.Empty;

    public string OriginatingDataSystem { get; set; } = string.Empty;
    public string OriginatingVendorTool { get; set; } = string.Empty;
    public string? DataSystemPath { get; set; }

    /// <summary>
    /// New field — confirmed distinct from OriginatingDataSystem in the
    /// gfr-edg-findings-dev export (e.g. dataSystem="NetApp" while
    /// originatingDataSystem="smb" on the same record). Named
    /// SourceSystemPlatform rather than DataSystem to avoid colliding with
    /// the existing FileFinding.DataSystem compatibility alias below, which
    /// is already wired into ParquetIngestionWorkingFileStrategy.cs and
    /// IngestionService.cs (CSV/Excel column mapping + reflection lookup).
    /// Maps to DynamoDB attribute "dataSystem".
    /// </summary>
    public string? SourceSystemPlatform { get; set; }

    /// <summary>
    /// Error category for findings with FindingType = "Error".
    /// e.g. "TimeoutException", "ConnectionRefused".
    /// Confirmed present in both gfr-edg-findings-dev and gfr-edg-rejected-dev samples.
    /// Empty string for non-error findings.
    /// </summary>
    public string? ErrorCategory { get; set; }

    // ── Optional metadata ─────────────────────────────────────────────────────
    public DateTime? LastModifiedDateUtc { get; set; }
    public DateTime? CreatedDateUtc { get; set; }
    public DateTime? LastAccessedDateUtc { get; set; }
    public DateTime? DetectionDateUtc { get; set; }
    public string? SiteOwner { get; set; }
    public string? FileOwner { get; set; }
    public string? BusinessUnit { get; set; }
    public string? Division { get; set; }
    public string? Department { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? PolicyName { get; set; }
    public string? PolicyId { get; set; }
    public string? FindingReason { get; set; }
    public string? RiskLevel { get; set; }
    public string? SensitivityLabel { get; set; }
    public string? RecommendedAction { get; set; }

    // ── Workflow fields ───────────────────────────────────────────────────────
    public string? OriginalFileLocation { get; set; }
    public DateTime? QuarantineDateUtc { get; set; }
    public DateTime? RestoredDateUtc { get; set; }
    public DateTime? DeletedDateUtc { get; set; }

    /// <summary>Date file was marked as Exclusion/Exception.</summary>
    public DateTime? ExceptionDateUtc { get; set; }

    public string? RestorationTicketIdentifier { get; set; }
    public string? RestorationRequestorEmail { get; set; }
    public string? RestorationComment { get; set; }

    // ── GFR workflow status ───────────────────────────────────────────────────
    public FileStatus Status { get; set; } = FileStatus.NotYetStarted;

    // ── Ingestion pipeline fields (NOT persisted to DynamoDB) ─────────────────
    public bool IsValid { get; set; } = true;

    /// <summary>Validation error message set during ingestion. Not stored in DynamoDB.</summary>
    public string IngestionErrorReason { get; set; } = string.Empty;

    // ── Compatibility properties ───────────────────────────────────────────────
    // Allow existing services to keep working without changes.

    public string ErrorReason
    {
        get => IngestionErrorReason;
        set => IngestionErrorReason = value ?? string.Empty;
    }

    public string FileName
    {
        get => FindingFileName;
        set => FindingFileName = value ?? string.Empty;
    }

    public string FilePath
    {
        get => CurrentFileLocation;
        set => CurrentFileLocation = value ?? string.Empty;
    }

    public string SourceSystem
    {
        get => OriginatingDataSystem;
        set => OriginatingDataSystem = value ?? string.Empty;
    }

    public long FileSize
    {
        get => FindingFileSizeBytes ?? 0;
        set => FindingFileSizeBytes = value;
    }

    public string? QuarantinePath
    {
        get => Status == FileStatus.QuarantineComplete ? CurrentFileLocation : null;
        set => CurrentFileLocation = value ?? string.Empty;
    }

    public DateTime LastModifiedDate
    {
        get => LastModifiedDateUtc ?? DateTime.MinValue;
        set => LastModifiedDateUtc = value;
    }

    public string? IngestionId
    {
        get => IngestionJobId;
        set => IngestionJobId = value;
    }

    public string UploadedBy
    {
        get => UserName;
        set => UserName = value ?? "System";
    }

    public DateTime LoadDate
    {
        get => LoadDateUtc;
        set => LoadDateUtc = value;
    }

    public DateTime UpdatedDate
    {
        get => LastUpdateDateUtc;
        set => LastUpdateDateUtc = value;
    }

    public DateTime? QuarantineDate
    {
        get => QuarantineDateUtc;
        set => QuarantineDateUtc = value;
    }

    public string DataSystem
    {
        get => OriginatingDataSystem;
        set => OriginatingDataSystem = value ?? string.Empty;
    }
}