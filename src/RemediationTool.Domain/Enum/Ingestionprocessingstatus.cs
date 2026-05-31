namespace RemediationTool.Domain.Enums;

/// <summary>
/// Represents the internal processing status of a FileFinding record
/// within the ingestion pipeline only.
///
/// This is intentionally separate from <see cref="FindingType"/>, which
/// is the spec-defined data model lifecycle (Obsolete → Quarantined → Deleted etc.).
///
/// IngestionProcessingStatus tracks whether this record was successfully
/// loaded, rejected, or encountered a transient failure during ingestion.
/// It is NOT persisted to the final data store — it is used only during
/// the ingestion pipeline to drive retry logic and rejected-row reporting.
/// </summary>
public enum IngestionProcessingStatus
{
    /// <summary>Record parsed and passed all validation checks. Ready for persistence.</summary>
    Valid,

    /// <summary>Record failed one or more validation rules and was rejected.</summary>
    Rejected,

    /// <summary>Record encountered a transient or unexpected error during processing.</summary>
    Failed
}