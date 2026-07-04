namespace RemediationTool.Domain.Entities;

/// <summary>
/// Represents an error or rejected record written to gfr-edg-rejected-dev.
///
/// This table stores records where FindingType = "Error" — either:
///   - CSV row validation failures (fieldName/rejectedValue/errorReason populated)
///   - Infrastructure errors from file scanning (errorCategory/stackTrace populated)
///
/// Schema confirmed from gfr-edg-rejected-dev sample (2026-07-01):
///   Primary key: id  (individual row GUID)
///   Job link:    uid (= reportUid from gfr-edg-reports-dev)
/// </summary>
public class RejectedRowDetail
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Primary key — unique GUID for this error record. Maps to "id" attribute.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Job link — reportUid of the parent ingestion job. Maps to "uid" attribute.
    /// Same pattern as FileFinding.uid confirmed by Deepan (2026-07-01).</summary>
    public string Uid { get; set; } = string.Empty;

    // ── Compatibility aliases (kept so existing callers do not break) ─────────

    /// <summary>Legacy alias for Id — used by existing IngestionService code.</summary>
    public string RejectedRowId
    {
        get => Id;
        set => Id = value;
    }

    /// <summary>Legacy alias for Uid — used by existing IngestionService code.</summary>
    public string JobId
    {
        get => Uid;
        set => Uid = value;
    }

    // ── File / row metadata ───────────────────────────────────────────────────

    public string InboundFileName { get; set; } = string.Empty;
    public string? SourceRecordId { get; set; }
    public string? FindingFileName { get; set; }
    public string? FindingType { get; set; }
    public string? UserName { get; set; }
    public int RowNumber { get; set; }

    // ── Validation failure fields (CSV row-level rejections) ──────────────────

    public string FieldName { get; set; } = string.Empty;
    public string? RejectedValue { get; set; }
    public string ErrorReason { get; set; } = string.Empty;
    public string? RawRowJson { get; set; }

    // ── Infrastructure / scanning error fields ────────────────────────────────

    /// <summary>Error category for infrastructure failures e.g. "TimeoutException".
    /// New field confirmed in gfr-edg-rejected-dev sample.</summary>
    public string? ErrorCategory { get; set; }

    /// <summary>Full .NET exception stack trace for infrastructure errors.
    /// New field confirmed in gfr-edg-rejected-dev sample.</summary>
    public string? StackTrace { get; set; }

    // ── Finding fields (same shape as FileFinding for infrastructure errors) ───

    public string? CurrentFileLocation { get; set; }
    public string? DataSystem { get; set; }
    public string? FileOwner { get; set; }
    public string? SiteOwner { get; set; }
    public string? FindingFileFormat { get; set; }
    public long? FindingFileSizeBytes { get; set; }
    public string? OriginatingDataSystem { get; set; }
    public string? OriginatingVendorTool { get; set; }
    public string? QuarantineDate { get; set; }
    public int RecordVersionId { get; set; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    public DateTime ErrorDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FileLastModifiedOn { get; set; }

    // ── Status ────────────────────────────────────────────────────────────────

    public string Status { get; set; } = "Error";

    // ── Compatibility alias ───────────────────────────────────────────────────
    public DateTime CreatedAtUtc
    {
        get => ErrorDateUtc;
        set => ErrorDateUtc = value;
    }
}