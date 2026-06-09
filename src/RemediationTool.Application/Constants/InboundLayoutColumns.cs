namespace RemediationTool.Application.Constants;

/// <summary>
/// Column name constants for the inbound file layout (CSV/XLSX uploads).
///
/// These string values must match exactly the header names used in the inbound files
/// (case-insensitive comparison is applied during header normalisation).
///
/// Column classification follows the Inbound File Layout tab of the requirements spec:
///   - RequiredColumns: must be present and non-null or the record is rejected.
///   - OptionalColumns: may be absent or null without causing rejection.
///
/// Note: Header normalisation strips spaces, underscores, and hyphens before comparison,
/// so "Finding_File_Name", "Finding File Name", and "FindingFileName" all match.
/// </summary>
public static class InboundLayoutColumns
{
    // =========================================================================
    // CORE INBOUND COLUMNS
    // =========================================================================

    public const string SourceRecordId = "ID";
    public const string InboundFileName = "Inbound_File_Name";

    public const string FindingFileName = "Finding_File_Name";
    public const string FindingFileFormat = "Finding File Format";
    public const string FindingFileSize = "Finding_File_Size";
    public const string CurrentFileLocation = "Current_File_Location";
    public const string FindingType = "Finding_Type";

    /// <summary>
    /// Specific NetApp drive path including parent folder
    /// (e.g. dc1f7c2nasv3.es.ad.adp.com/enterprise).
    /// Mandatory per the Inbound File Layout specification.
    /// </summary>
    public const string DataSystem = "Data_System";

    public const string OriginatingDataSystem = "Originating_Data_System";
    public const string OriginatingVendorTool = "Originating_Vendor_Tool";

    public const string OriginalFileLocation = "Original_File_Location";
    public const string QuarantineDate = "Quarantine_Date";

    public const string SiteOwner = "Site_Owner";
    public const string FileOwner = "File_Owner";

    // =========================================================================
    // RESTORATION WORKFLOW COLUMNS
    // These are populated later in the restoration workflow, not on initial load.
    // =========================================================================

    public const string RestorationTicketIdentifier = "Restoration_Ticket_Identifier";
    public const string RestorationRequestorEmail = "Restoration_Requestor_Email";
    public const string RestorationComment = "Restoration_Comment";

    // =========================================================================
    // COLUMN CLASSIFICATION
    // =========================================================================

    /// <summary>
    /// Columns that MUST be present and non-null in the inbound file.
    /// Records missing any of these values are rejected and logged.
    /// Source: Inbound File Layout tab — Mandatory (M) columns.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredColumns = new[]
    {
        FindingFileName,
        FindingFileFormat,
        CurrentFileLocation,
        FindingType,
        DataSystem,           // Added: Mandatory per spec
        OriginatingDataSystem,
        OriginatingVendorTool
    };

    /// <summary>
    /// Columns that may be absent or empty without causing record rejection.
    /// Source: Inbound File Layout tab — Optional (O) columns, plus extended metadata
    /// columns supported for future phases.
    /// </summary>
    public static readonly IReadOnlyList<string> OptionalColumns = new[]
    {
        SourceRecordId,
        InboundFileName,
        FindingFileSize,
        OriginalFileLocation,
        QuarantineDate,
        SiteOwner,
        FileOwner,
        RestorationTicketIdentifier,
        RestorationRequestorEmail,
        RestorationComment
    };

    /// <summary>All known columns (required + optional). Used for header presence checks.</summary>
    public static readonly IReadOnlyList<string> AllKnownColumns =
        RequiredColumns
            .Concat(OptionalColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}