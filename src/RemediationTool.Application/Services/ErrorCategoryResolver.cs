using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Single place in the codebase that decides which ErrorCategory to assign
/// to a failed ingestion row or remediation action, based on the exception type
/// or the specific pre-check condition that triggered the failure.
///
/// Rules are taken directly from the Error Categories tab of the requirements
/// spreadsheet. Each method maps a real failure scenario to the correct enum
/// value so it can be stored in the errorCategory column.
///
/// Usage:
///   Pre-check: file.ErrorCategory = ErrorCategoryResolver.SourceFileMissing().ToString();
///   Exception: file.ErrorCategory = ErrorCategoryResolver.FromException(ex).ToString();
///   Validation: row.ErrorCategory = ErrorCategoryResolver.ValidationFailure(error.PropertyName).ToString();
/// </summary>
public static class ErrorCategoryResolver
{
    // ── Exception-based resolution ────────────────────────────────────────────
    // Called from catch(Exception ex) blocks in QuarantineService,
    // RestoreService, and DeleteService when the failure came from an exception.

    public static ErrorCategory FromException(Exception ex) => ex switch
    {
        // Insufficient rights to read, quarantine, restore or delete
        UnauthorizedAccessException
            => ErrorCategory.PermissionDenied,

        // File is read-only, locked, checked out, or system-protected
        IOException ioe when IsLockedOrCheckedOut(ioe)
            => ErrorCategory.FileLockedOrCheckedOut,

        IOException ioe when IsReadOnlyOrProtected(ioe)
            => ErrorCategory.ReadOnlyOrSystemProtected,

        // Network share unreachable / certificate / token failure
        IOException ioe when IsConnectivityFailure(ioe)
            => ErrorCategory.AuthenticationOrCertificateFailure,

        // Rate limiting / throttling from the platform
        IOException ioe when IsThrottled(ioe)
            => ErrorCategory.RateLimitingOrThrottling,

        // Unsupported file type — tool cannot process this format
        NotSupportedException
            => ErrorCategory.UnsupportedFileType,

        // Operation timed out — remote share or system latency
        TimeoutException
            => ErrorCategory.AuthenticationOrCertificateFailure,

        // Task cancelled — typically a retry timeout exhausted
        OperationCanceledException
            => ErrorCategory.RetryExhausted,

        // System temporarily unavailable during restore
        InvalidOperationException ioe when IsSystemUnavailable(ioe)
            => ErrorCategory.RestorationSystemUnavailable,

        // Restore/delete target conflict
        InvalidOperationException ioe when IsConflict(ioe)
            => ErrorCategory.RestorationTargetConflict,

        // Invalid file/row shape or unsupported upload content
        InvalidDataException
            => ErrorCategory.MalformedInputRow,

        // Any other unclassified failure — does not match any known category
        _ => ErrorCategory.Others
    };

    // ── Ingestion validation resolution ───────────────────────────────────────

    /// <summary>
    /// Row failed FluentValidation. When possible, maps the validation failure
    /// to a more specific category for reporting.
    /// </summary>
    public static ErrorCategory ValidationFailure(string? propertyName, string? errorMessage = null)
    {
        var normalizedProperty = propertyName?.Trim() ?? string.Empty;
        var normalizedMessage = errorMessage?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalizedMessage.Contains("required") || normalizedMessage.Contains("must not be empty"))
            return ErrorCategory.MissingRequiredField;

        if (normalizedMessage.Contains("invalid") || normalizedMessage.Contains("must be one of"))
            return ErrorCategory.InvalidAllowedValue;

        if (normalizedMessage.Contains("date") || normalizedMessage.Contains("number") || normalizedMessage.Contains("size"))
            return ErrorCategory.InvalidDataType;

        return string.IsNullOrWhiteSpace(normalizedProperty)
            ? ErrorCategory.ValidationError
            : ErrorCategory.ValidationError;
    }

    /// <summary>
    /// Row could not be parsed before normal validation could run.
    /// </summary>
    public static ErrorCategory MalformedInputRow()
        => ErrorCategory.MalformedInputRow;

    // ── Pre-check condition resolution ────────────────────────────────────────
    // Called when a specific condition is detected BEFORE the operation
    // runs — no exception has been thrown yet at this point.

    /// <summary>
    /// Source file does not exist at its expected path.
    /// QuarantineService: if (!File.Exists(sourcePath))
    /// Spreadsheet row 2 — Missing at Source. Actionable: false.
    /// </summary>
    public static ErrorCategory SourceFileMissing()
        => ErrorCategory.MissingAtSource;

    /// <summary>
    /// Quarantine file not found when attempting a restore.
    /// RestoreService: if (!File.Exists(quarantinePath))
    /// Spreadsheet row 10 — Restoration – Quarantine File Missing. Actionable: true, Priority: High.
    /// </summary>
    public static ErrorCategory QuarantineFileMissing()
        => ErrorCategory.RestorationQuarantineFileMissing;

    /// <summary>
    /// Original file path is null or empty — cannot determine restore destination.
    /// RestoreService: if (string.IsNullOrWhiteSpace(originalPath))
    /// Spreadsheet row 9 — Restoration – Target Path Missing. Actionable: true, Priority: High.
    /// </summary>
    public static ErrorCategory TargetPathMissing()
        => ErrorCategory.RestorationTargetPathMissing;

    /// <summary>
    /// Required metadata (path, owner, timestamps) missing or invalid for restore.
    /// RestoreService: if metadata fields are null/empty before restore starts.
    /// Spreadsheet row 13 — Restoration – Metadata Integrity Failure. Actionable: true, Priority: High.
    /// </summary>
    public static ErrorCategory MetadataIntegrityFailure()
        => ErrorCategory.RestorationMetadataIntegrityFailure;

    /// <summary>
    /// File was already restored — duplicate restore attempt detected.
    /// RestoreService: if status is already RestorationComplete.
    /// Spreadsheet row 14 — Restoration – Duplicate Restore Attempt. Actionable: false, Priority: Low.
    /// </summary>
    public static ErrorCategory DuplicateRestoreAttempt()
        => ErrorCategory.RestorationDuplicateRestoreAttempt;

    /// <summary>
    /// Some files succeeded, some failed in a bulk restore operation.
    /// RestoreAllAsync: after loop, if partial failures detected.
    /// Spreadsheet row 12 — Restoration – Partial Restore Failure. Actionable: true, Priority: High.
    /// </summary>
    public static ErrorCategory PartialRestoreFailure()
        => ErrorCategory.RestorationPartialRestoreFailure;

    public static ErrorCategory RestoreTargetConflict()
        => ErrorCategory.RestorationTargetConflict;

    public static ErrorCategory RetentionNotMet()
        => ErrorCategory.DeletionRetentionNotMet;

    public static ErrorCategory DeleteQuarantineFileMissing()
        => ErrorCategory.DeletionQuarantineFileMissing;

    public static ErrorCategory DuplicateDeleteAttempt()
        => ErrorCategory.DeletionDuplicateAttempt;

    public static ErrorCategory PartialDeleteFailure()
        => ErrorCategory.DeletionPartialFailure;

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsLockedOrCheckedOut(IOException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("sharing violation")
            || msg.Contains("locked")
            || msg.Contains("checked out")
            || msg.Contains("in use")
            || msg.Contains("being used by another process");
    }

    private static bool IsReadOnlyOrProtected(IOException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("read-only")
            || msg.Contains("readonly")
            || msg.Contains("write protected")
            || msg.Contains("access is denied");
    }

    private static bool IsConnectivityFailure(IOException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("network")
            || msg.Contains("unreachable")
            || msg.Contains("no such host")
            || msg.Contains("cannot access")
            || msg.Contains("the network path")
            || msg.Contains("unc")
            || msg.Contains("host not found")
            || msg.Contains("connection refused")
            || msg.Contains("certificate")
            || msg.Contains("token");
    }

    private static bool IsThrottled(IOException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("throttl")
            || msg.Contains("rate limit")
            || msg.Contains("too many requests")
            || msg.Contains("429");
    }

    private static bool IsSystemUnavailable(InvalidOperationException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("unavailable")
            || msg.Contains("not available")
            || msg.Contains("service unavailable");
    }

    private static bool IsConflict(InvalidOperationException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("conflict")
            || msg.Contains("already exists")
            || msg.Contains("duplicate target");
    }
}
