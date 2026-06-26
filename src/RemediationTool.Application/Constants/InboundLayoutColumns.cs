namespace RemediationTool.Application.Constants;

/// <summary>
/// Column name constants for the inbound EDG report file (CSV/XLSX).
/// Header matching is case-insensitive and ignores spaces/underscores/hyphens.
/// </summary>
public static class InboundLayoutColumns
{
    // ── Core required columns ─────────────────────────────────────────────────
    public const string SourceRecordId = "ID";
    public const string InboundFileName = "Inbound_File_Name";
    public const string FindingFileName = "Finding_File_Name";
    public const string FindingFileFormat = "Finding File Format";
    public const string FindingFileSize = "Finding_File_Size";
    public const string CurrentFileLocation = "Current_File_Location";
    public const string FindingType = "Finding_Type";
    public const string DataSystem = "Data_System";
    public const string OriginatingDataSystem = "Originating_Data_System";
    public const string OriginatingVendorTool = "Originating_Vendor_Tool";

    // ── Workflow columns ──────────────────────────────────────────────────────
    public const string OriginalFileLocation = "Original_File_Location";
    public const string QuarantineDate = "Quarantine_Date";
    public const string SiteOwner = "Site_Owner";
    public const string FileOwner = "File_Owner";
    public const string RestorationTicketIdentifier = "Restoration_Ticket_Identifier";
    public const string RestorationRequestorEmail = "Restoration_Requestor_Email";
    public const string RestorationComment = "Restoration_Comment";

    // ── Extended metadata columns ─────────────────────────────────────────────
    public const string LastModifiedDate = "Last_Modified_Date";
    public const string CreatedDate = "Created_Date";
    public const string LastAccessedDate = "Last_Accessed_Date";
    public const string DetectionDate = "Detection_Date";
    public const string BusinessUnit = "Business_Unit";
    public const string Division = "Division";
    public const string Department = "Department";
    public const string Region = "Region";
    public const string Country = "Country";
    public const string PolicyName = "Policy_Name";
    public const string PolicyId = "Policy_ID";
    public const string FindingReason = "Finding_Reason";
    public const string RiskLevel = "Risk_Level";
    public const string SensitivityLabel = "Sensitivity_Label";
    public const string RecommendedAction = "Recommended_Action";

    // ── Required columns — must be present in every upload ───────────────────
    public static readonly IReadOnlyList<string> RequiredColumns = new[]
    {
        FindingFileName,
        FindingFileFormat,
        CurrentFileLocation,
        FindingType,
        OriginatingDataSystem,
        OriginatingVendorTool
    };

    // ── Optional columns — may be absent without causing row rejection ────────
    public static readonly IReadOnlyList<string> OptionalColumns = new[]
    {
        SourceRecordId,
        InboundFileName,
        FindingFileSize,
        DataSystem,
        OriginalFileLocation,
        QuarantineDate,
        SiteOwner,
        FileOwner,
        RestorationTicketIdentifier,
        RestorationRequestorEmail,
        RestorationComment,
        LastModifiedDate,
        CreatedDate,
        LastAccessedDate,
        DetectionDate,
        BusinessUnit,
        Division,
        Department,
        Region,
        Country,
        PolicyName,
        PolicyId,
        FindingReason,
        RiskLevel,
        SensitivityLabel,
        RecommendedAction
    };

    /// <summary>All known columns (required + optional).</summary>
    public static readonly IReadOnlyList<string> AllKnownColumns =
        RequiredColumns.Concat(OptionalColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}