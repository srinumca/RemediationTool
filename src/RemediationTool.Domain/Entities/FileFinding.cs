using RemediationTool.Domain;

namespace RemediationTool.Domain.Entities;

/// <summary>
/// Core domain entity representing a single file finding.
/// FindingType is a plain string. Status tracks GFR workflow stage.
/// </summary>
public class FileFinding
{
    private string _findingType = string.Empty;
    private FileStatus _status = FileStatus.NotYetStarted;

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
    ///
    /// Initial ingestion status rule:
    /// - Obsolete lands as NotYetStarted
    /// - Every other finding type lands with the same value as FindingType in the persisted status column
    /// After ingestion, quarantine/restore/delete services own all lifecycle transitions.
    /// </summary>
    public string FindingType
    {
        get => _findingType;
        set
        {
            _findingType = value ?? string.Empty;

            if (IsInitialStatusAssignmentAllowed())
            {
                Status = ResolveInitialStatusFromFindingType(_findingType);
                StatusColumnValue = ResolveInitialStatusColumnValueFromFindingType(_findingType);
            }
        }
    }

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
    public FileStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            StatusColumnValue = value.ToString();
        }
    }

    /// <summary>
    /// Exact string value to persist into the database status column.
    /// During initial ingestion this can intentionally differ from the FileStatus enum
    /// so non-obsolete rows can store status exactly equal to FindingType.
    /// Lifecycle services update this automatically through the Status setter.
    /// </summary>
    public string StatusColumnValue { get; set; } = FileStatus.NotYetStarted.ToString();

    // ── Ingestion pipeline fields (NOT persisted to DynamoDB) ─────────────────
    public bool IsValid { get; set; } = true;

    /// <summary>Validation error message set during ingestion. Not stored in DynamoDB.</summary>
    public string IngestionErrorReason { get; set; } = string.Empty;

    // ── Initial ingestion status mapping ──────────────────────────────────────

    /// <summary>
    /// Resolves the in-memory workflow status for a newly ingested row.
    /// The database status column is resolved separately by ResolveInitialStatusColumnValueFromFindingType.
    /// </summary>
    public static FileStatus ResolveInitialStatusFromFindingType(string? findingType)
    {
        var normalized = NormalizeStatusValue(findingType);

        return normalized switch
        {
            "obsolete" => FileStatus.NotYetStarted,
            "quarantined" => FileStatus.Quarantined,
            "totalpendingquarantined" => FileStatus.TotalPendingQuarantined,
            "restoration" => FileStatus.Restoration,
            "restored" => FileStatus.Restored,
            "deleted" => FileStatus.Deleted,
            "exception" => FileStatus.Exception,
            "exclusion" => FileStatus.Exclusion,
            "notobsolete" => FileStatus.NotObsolete,
            "error" => FileStatus.Error,
            _ => FileStatus.NotYetStarted
        };
    }

    /// <summary>
    /// Resolves the exact value stored in the status column during ingestion.
    /// Business rule: Obsolete -> NotYetStarted; all other values -> same as FindingType.
    /// </summary>
    public static string ResolveInitialStatusColumnValueFromFindingType(string? findingType)
    {
        if (string.IsNullOrWhiteSpace(findingType))
            return FileStatus.NotYetStarted.ToString();

        var trimmedFindingType = findingType.Trim();
        return NormalizeStatusValue(trimmedFindingType) == "obsolete"
            ? FileStatus.NotYetStarted.ToString()
            : trimmedFindingType;
    }

    public static FileStatus ResolveStatusFromStoredValue(string? statusValue)
    {
        if (string.IsNullOrWhiteSpace(statusValue))
            return FileStatus.NotYetStarted;

        if (Enum.TryParse<FileStatus>(statusValue, ignoreCase: true, out var parsedStatus))
            return parsedStatus;

        return NormalizeStatusValue(statusValue) switch
        {
            "notobsolete" => FileStatus.NotObsolete,
            "totalpendingquarantined" => FileStatus.TotalPendingQuarantined,
            _ => FileStatus.NotYetStarted
        };
    }

    private bool IsInitialStatusAssignmentAllowed()
        => _status == FileStatus.NotYetStarted
           && string.Equals(StatusColumnValue, FileStatus.NotYetStarted.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatusValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        return value
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

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
