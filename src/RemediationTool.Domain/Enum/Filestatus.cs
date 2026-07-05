namespace RemediationTool.Domain;

/// <summary>
/// Represents the current processing stage of a file record
/// in the GFR remediation workflow.
///
/// Kept in RemediationTool.Domain namespace (no sub-namespace)
/// to match existing FileFinding.cs usage: FileStatus.Loaded
/// </summary>
public enum FileStatus
{
    /// <summary>File ingested — awaiting processing. Default on ingest.</summary>
    NotYetStarted = 0,

    /// <summary>File queued and waiting to be quarantined by DataSync.</summary>
    PendingQuarantine = 1,

    /// <summary>File queued and waiting to be restored.</summary>
    PendingRestore = 2,

    /// <summary>File action currently in progress (DataSync running).</summary>
    InProgress = 3,

    /// <summary>File successfully moved to quarantine. Stub placed at source.</summary>
    QuarantineComplete = 4,

    /// <summary>File successfully restored from quarantine to original location.</summary>
    RestorationComplete = 5,

    /// <summary>File marked as an exception — excluded from all actions.</summary>
    Exception = 6,

    /// <summary>Error occurred during processing — requires investigation.</summary>
    Error = 7,

    /// <summary>File permanently deleted. Irreversible.</summary>
    DeletionComplete = 8,

    // ── Initial ingestion values that can mirror FindingType exactly in storage ─
    // These are kept separate from lifecycle-complete statuses because the inbound
    // file may already contain non-obsolete records whose initial Status column
    // must match FindingType before any tool lifecycle action runs.
    Restoration = 20,
    Exclusion = 21,
    TotalPendingQuarantined = 22,
    NotObsolete = 23,

    // ── Legacy values — kept for backward compatibility ────────────────────────
    Loaded = 100,
    Quarantined = 101,
    Restored = 102,
    Deleted = 103,
    NotEligible = 104,
    Missing = 105,
    Failed = 106
}
