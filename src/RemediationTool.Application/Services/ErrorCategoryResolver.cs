using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

/// <summary>
/// Single place in the codebase that decides which ErrorCategory to assign
/// to a failed remediation action, based on the exception type or the
/// specific pre-check condition that triggered the failure.
///
/// Rules are taken directly from the Error Categories tab of the
/// requirements spreadsheet. Each method maps a real failure scenario
/// to the correct enum value so it can be stored in the errorCategory
/// column of gfr-edg-findings-dev.
///
/// Usage:
///   Pre-check: file.ErrorCategory = ErrorCategoryResolver.SourceFileMissing().ToString();
///   Exception: file.ErrorCategory = ErrorCategoryResolver.FromException(ex).ToString();
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

        // File is read-only or system-protected
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

        // Any other unclassified failure — does not match any known category
        _ => ErrorCategory.Others
    };

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

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsReadOnlyOrProtected(IOException ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("read-only")
            || msg.Contains("readonly")
            || msg.Contains("write protected")
            || msg.Contains("access is denied")
            || msg.Contains("sharing violation");
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
}