namespace RemediationTool.Application.Constants;

/// <summary>
/// Finding type string constants — values received from EDG inbound CSV/XLSX.
/// Used by FileFindingValidator and throughout the ingestion pipeline.
/// FindingType is stored and compared as a plain string (not an enum)
/// for extensibility with future source systems.
/// </summary>
public static class FindingTypes
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

    /// <summary>All allowed Finding_Type values accepted during ingestion.</summary>
    public static readonly IReadOnlyList<string> AllAllowedTypes = new[]
    {
        Obsolete, Quarantined, Restored, Deleted, NotObsolete,
        Exclusion, TotalPendingQuarantined, Restoration, Exception, Error
    };
}