namespace RemediationTool.Application.Constants;

/// <summary>
/// Finding type string constants matching values in the inbound CSV/XLSX.
/// Stored and compared as plain strings throughout the system.
/// No enum — allows easy extension for future source systems.
/// </summary>
public static class FindingType
{
    public const string Obsolete = "Obsolete";
    public const string Quarantined = "Quarantined";
    public const string Restored = "Restored";
    public const string Deleted = "Deleted";
    public const string NotObsolete = "Not Obsolete";
    public const string Exclusion = "Exclusion";
    public const string TotalPendingQuarantined = "TotalPendingQuarantined";
    public const string Restoration = "Restoration";
    public const string Exception = "Exception";
    public const string Error = "Error";

    /// <summary>All allowed Finding_Type values during ingestion validation.</summary>
    public static readonly IReadOnlyList<string> AllAllowedTypes = new[]
    {
        Obsolete, Quarantined, Restored, Deleted, NotObsolete,
        Exclusion, TotalPendingQuarantined, Restoration, Exception, Error
    };
}