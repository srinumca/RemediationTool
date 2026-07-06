using System.Text.Json.Serialization;

namespace RemediationTool.Domain.Enums;

/// <summary>
/// Standardised error categories for ingestion validation failures and remediation action failures,
/// as defined in the Error Categories tab of the requirements specification.
///
/// Used on <see cref="RemediationTool.Domain.Entities.FileFinding.ErrorCategory"/>
/// to classify why a quarantine, deletion, or restoration action failed.
/// Also used on <see cref="RemediationTool.Domain.Entities.RejectedRowDetail"/>
/// to classify why an inbound row was rejected during ingestion.
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

    /// <summary>
    /// File is locked, checked out, or open by another process/user.
    /// Actionable: true — retry after lock is released or use admin intervention.
    /// </summary>
    FileLockedOrCheckedOut,

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
    // Processing / ingestion errors
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

    /// <summary>
    /// Row failed ingestion validation.
    /// Actionable: true — source data should be corrected and re-uploaded.
    /// </summary>
    ValidationError,

    /// <summary>
    /// Row could not be parsed due to malformed CSV/XLSX structure or invalid row shape.
    /// Actionable: true — source file should be corrected and re-uploaded.
    /// </summary>
    MalformedInputRow,

    /// <summary>
    /// Required inbound field is missing.
    /// Actionable: true — source data should be corrected and re-uploaded.
    /// </summary>
    MissingRequiredField,

    /// <summary>
    /// Inbound value has an invalid data type, such as a non-date value in a date field.
    /// Actionable: true — source data should be corrected and re-uploaded.
    /// </summary>
    InvalidDataType,

    /// <summary>
    /// Inbound value is outside the allowed value list.
    /// Actionable: true — source data should be corrected and re-uploaded.
    /// </summary>
    InvalidAllowedValue,

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

    /// <summary>
    /// Restore target already contains a file/object that may conflict with the restore.
    /// Actionable: true — requires manual confirmation or conflict policy.
    /// </summary>
    RestorationTargetConflict,

    // ---------------------------------------------------------------
    // Deletion-specific errors
    // ---------------------------------------------------------------

    /// <summary>
    /// File is not old enough to be deleted based on retention policy.
    /// Actionable: false until retention period is met.
    /// </summary>
    DeletionRetentionNotMet,

    /// <summary>
    /// Quarantined file was missing when deletion was attempted.
    /// Actionable: true — requires investigation of quarantine store.
    /// </summary>
    DeletionQuarantineFileMissing,

    /// <summary>
    /// A subset of files failed during a bulk delete operation.
    /// Actionable: true — requires manual investigation.
    /// </summary>
    DeletionPartialFailure,

    /// <summary>
    /// Delete action was attempted more than once after deletion had already completed.
    /// Actionable: false.
    /// </summary>
    DeletionDuplicateAttempt,

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
