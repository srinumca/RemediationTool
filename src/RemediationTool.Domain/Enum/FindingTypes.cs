namespace RemediationTool.Application.Constants;

/// <summary>
/// Finding type constants representing the EDG classification
/// of each file record as received in the inbound CSV/XLSX.
///
/// These values come directly from the EDG report — they are
/// NOT the same as FileStatus (which represents the GFR workflow stage).
///
/// FindingType = what EDG says about the file
/// FileStatus  = what GFR is doing about it
///
/// Extensible for future sources (Confluence, SharePoint, etc.)
/// </summary>
public static class FindingTypes
{
    // -------------------------------------------------------------------------
    // Core EDG finding types
    // -------------------------------------------------------------------------

    /// <summary>File exceeds retention threshold — primary input finding type.</summary>
    public const string Obsolete = "Obsolete";

    /// <summary>File already quarantined by a previous process.</summary>
    public const string Quarantined = "Quarantined";

    /// <summary>File has been restored from quarantine.</summary>
    public const string Restored = "Restored";

    /// <summary>File has been permanently deleted.</summary>
    public const string Deleted = "Deleted";

    /// <summary>File was evaluated but found to not be obsolete.</summary>
    public const string NotObsolete = "Not Obsolete";

    /// <summary>File has been explicitly excluded from remediation.</summary>
    public const string Exclusion = "Exclusion";

    // -------------------------------------------------------------------------
    // Extended finding types — added for GFR UI alignment
    // -------------------------------------------------------------------------

    /// <summary>File is pending quarantine (waiting to be processed by DataSync).</summary>
    public const string TotalPendingQuarantined = "TotalPendingQuarantined";

    /// <summary>File is being restored — restoration workflow in progress.</summary>
    public const string Restoration = "Restoration";

    /// <summary>File has been flagged as an exception.</summary>
    public const string Exception = "Exception";

    /// <summary>File encountered an error during processing.</summary>
    public const string Error = "Error";

    // -------------------------------------------------------------------------
    // All allowed values — used by validator
    // -------------------------------------------------------------------------

    public static readonly IReadOnlyList<string> AllAllowedTypes = new[]
    {
        Obsolete,
        Quarantined,
        Restored,
        Deleted,
        NotObsolete,
        Exclusion,
        TotalPendingQuarantined,
        Restoration,
        Exception,
        Error
    };

    /// <summary>
    /// Maps a FindingType to the initial FileStatus that should be
    /// assigned when a record is first ingested.
    /// </summary>
    public static FileStatus ToInitialFileStatus(string findingType) =>
        findingType?.Trim() switch
        {
            var ft when ft.Equals(Obsolete, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.PendingQuarantine,
            var ft when ft.Equals(TotalPendingQuarantined, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.PendingQuarantine,
            var ft when ft.Equals(Quarantined, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.QuarantineComplete,
            var ft when ft.Equals(Restoration, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.PendingRestore,
            var ft when ft.Equals(Restored, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.RestorationComplete,
            var ft when ft.Equals(Deleted, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.DeletionComplete,
            var ft when ft.Equals(Exception, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.Exception,
            var ft when ft.Equals(Error, StringComparison.OrdinalIgnoreCase) => RemediationTool.Domain.Enum.FileStatus.Error,
            _ => RemediationTool.Domain.Enum.FileStatus.NotYetStarted
        };
}