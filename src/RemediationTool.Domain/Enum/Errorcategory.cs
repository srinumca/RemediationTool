using System.Text.Json.Serialization;

namespace RemediationTool.Domain.Enums;

/// <summary>
/// Standardised error categories for remediation action failures,
/// as defined in the Error Categories tab of the requirements specification.
///
/// Used on <see cref="RemediationTool.Domain.Entities.FileFinding.ErrorCategory"/>
/// to classify why a quarantine, deletion, or restoration action failed.
/// This is distinct from ingestion validation errors, which are captured
/// in <see cref="RemediationTool.Domain.Entities.RejectedRowDetail"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorCategory
{
    /// <summary>No error. The record processed successfully.</summary>
    None,

    // ---------------------------------------------------------------
    // Source-state errors
    // ---------------------------------------------------------------

    /// <summary>
    /// File no longer exists at the source path at the time of action.
    /// Previously deleted or moved outside system control.
    /// Actionable: false.
    /// </summary>
    MissingAtSource,

    // ---------------------------------------------------------------
    // Permission and access errors
    // ---------------------------------------------------------------

    /// <summary>
    /// Insufficient rights to read, quarantine, restore, or delete the file.
    /// Actionable: true — requires permission remediation.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// File cannot be modified due to system constraints (read-only or system-protected).
    /// Actionable: true — requires system or admin intervention.
    /// </summary>
    ReadOnlyOrSystemProtected,

    // ---------------------------------------------------------------
    // Connectivity errors
    // ---------------------------------------------------------------

    /// <summary>
    /// Data system or folder is no longer connected.
    /// Token expiration or certificate validation failure.
    /// Actionable: true — priority: High.
    /// </summary>
    AuthenticationOrCertificateFailure,

    /// <summary>
    /// Platform-imposed rate limits or throttling exceeded.
    /// Actionable: true — retry after back-off.
    /// </summary>
    RateLimitingOrThrottling,

    // ---------------------------------------------------------------
    // Processing errors
    // ---------------------------------------------------------------

    /// <summary>
    /// File type cannot be processed by the remediation tool.
    /// Actionable: true — may require exclusion or manual handling.
    /// </summary>
    UnsupportedFileType,

    /// <summary>
    /// Maximum retry attempts reached without success.
    /// Actionable: true — requires manual investigation.
    /// </summary>
    RetryExhausted,

    // ---------------------------------------------------------------
    // Restoration-specific errors (priority: High unless stated)
    // ---------------------------------------------------------------

    /// <summary>
    /// Original file path or parent folder no longer exists.
    /// Actionable: true — priority: High.
    /// </summary>
    RestorationTargetPathMissing,

    /// <summary>
    /// File not found in quarantine or breadcrumb location at time of restore.
    /// Actionable: true — priority: High.
    /// </summary>
    RestorationQuarantineFileMissing,

    /// <summary>
    /// Target data system temporarily unavailable during restore attempt.
    /// Actionable: true — priority: Medium.
    /// </summary>
    RestorationSystemUnavailable,

    /// <summary>
    /// A subset of files failed during a bulk restore operation.
    /// Actionable: true — priority: High.
    /// </summary>
    RestorationPartialRestoreFailure,

    /// <summary>
    /// Required metadata (path, owner, timestamps) is missing or invalid for restore.
    /// Actionable: true — priority: High.
    /// </summary>
    RestorationMetadataIntegrityFailure,

    /// <summary>
    /// File was already restored, or a restore was previously completed for this record.
    /// Actionable: false — priority: Low.
    /// </summary>
    RestorationDuplicateRestoreAttempt,

    // ---------------------------------------------------------------
    // Catch-all
    // ---------------------------------------------------------------

    /// <summary>
    /// The error or exception does not match any of the defined categories.
    /// Requires manual investigation to determine the root cause.
    /// Actionable: true.
    /// </summary>
    Others
}