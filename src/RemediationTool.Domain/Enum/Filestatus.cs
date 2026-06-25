namespace RemediationTool.Domain.Enum;

/// <summary>
/// Represents the current processing stage of a file record
/// throughout the GFR remediation workflow.
///
/// Used on FileFinding.Status to track where a file is
/// in its lifecycle from ingestion through to final action.
///
/// Designed to be extensible for future source systems
/// (Confluence, SharePoint, OneDrive, etc.)
/// </summary>
public enum FileStatus
{
    /// <summary>
    /// File has been ingested and is awaiting processing.
    /// Initial status set during ingestion.
    /// </summary>
    NotYetStarted = 0,

    /// <summary>
    /// File is queued and waiting to be quarantined.
    /// Set after ingestion when FindingType = Obsolete.
    /// </summary>
    PendingQuarantine = 1,

    /// <summary>
    /// File is queued and waiting to be restored.
    /// Set when a restoration request is raised.
    /// </summary>
    PendingRestore = 2,

    /// <summary>
    /// File action is currently in progress
    /// (being quarantined, restored, or deleted by DataSync).
    /// </summary>
    InProgress = 3,

    /// <summary>
    /// File has been successfully moved to quarantine.
    /// DataSync completed. Stub file placed at source.
    /// </summary>
    QuarantineComplete = 4,

    /// <summary>
    /// File has been successfully restored from quarantine
    /// back to its original location.
    /// </summary>
    RestorationComplete = 5,

    /// <summary>
    /// File has been marked as an exception.
    /// Excluded from quarantine/deletion actions.
    /// </summary>
    Exception = 6,

    /// <summary>
    /// An error occurred during processing.
    /// Requires investigation and manual intervention.
    /// </summary>
    Error = 7,

    /// <summary>
    /// File has been permanently deleted.
    /// This action is irreversible.
    /// </summary>
    DeletionComplete = 8,

    // -------------------------------------------------------------------------
    // Legacy values — kept for backward compatibility with existing JSON data
    // -------------------------------------------------------------------------

    /// <summary>Legacy: maps to NotYetStarted.</summary>
    Loaded = 100,

    /// <summary>Legacy: maps to QuarantineComplete.</summary>
    Quarantined = 101,

    /// <summary>Legacy: maps to RestorationComplete.</summary>
    Restored = 102,

    /// <summary>Legacy: maps to DeletionComplete.</summary>
    Deleted = 103,

    /// <summary>Legacy: file not eligible for remediation.</summary>
    NotEligible = 104,

    /// <summary>Legacy: file missing at source.</summary>
    Missing = 105,

    /// <summary>Legacy: maps to Error.</summary>
    Failed = 106
}