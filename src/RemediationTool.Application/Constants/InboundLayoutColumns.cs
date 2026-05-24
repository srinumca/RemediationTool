using System;
using System.Collections.Generic;
using System.Linq;

namespace RemediationTool.Application.Constants;

public static class InboundLayoutColumns
{
    // ------------------------------------------------------------
    // Core inbound layout columns received from EDG / source feed
    // ------------------------------------------------------------

    public const string SourceRecordId = "ID";
    public const string InboundFileName = "Inbound_File_Name";

    public const string FindingFileName = "Finding_File_Name";
    public const string FindingFileFormat = "Finding File Format";
    public const string FindingFileSize = "Finding_File_Size";
    public const string CurrentFileLocation = "Current_File_Location";
    public const string FindingType = "Finding_Type";

    public const string OriginatingDataSystem = "Originating_Data_System";
    public const string OriginatingVendorTool = "Originating_Vendor_Tool";

    public const string OriginalFileLocation = "Original_File_Location";
    public const string QuarantineDate = "Quarantine_Date";

    public const string LastModifiedDate = "Last_Modified_Date";
    public const string CreatedDate = "Created_Date";
    public const string LastAccessedDate = "Last_Accessed_Date";

    public const string SiteOwner = "Site_Owner";
    public const string FileOwner = "File_Owner";
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

    public const string DetectionDate = "Detection_Date";
    public const string RecommendedAction = "Recommended_Action";

    // ------------------------------------------------------------
    // Restore-related fields may be empty during initial ingestion.
    // These become important later during restore flow.
    // ------------------------------------------------------------

    public const string RestorationTicketIdentifier = "Restoration_Ticket_Identifier";
    public const string RestorationRequestorEmail = "Restoration_Requestor_Email";
    public const string RestorationComment = "Restoration_Comment";

    // ------------------------------------------------------------
    // Minimum required fields for initial ingestion.
    // Keep this strict enough for data quality, but not too strict
    // that optional/client-specific fields block valid records.
    // ------------------------------------------------------------

    public static readonly IReadOnlyList<string> RequiredColumns = new List<string>
    {
        FindingFileName,
        FindingFileFormat,
        CurrentFileLocation,
        FindingType,
        OriginatingDataSystem,
        OriginatingVendorTool
    };

    public static readonly IReadOnlyList<string> OptionalColumns = new List<string>
    {
        SourceRecordId,
        InboundFileName,
        FindingFileSize,
        OriginalFileLocation,
        QuarantineDate,
        LastModifiedDate,
        CreatedDate,
        LastAccessedDate,
        SiteOwner,
        FileOwner,
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
        DetectionDate,
        RecommendedAction,
        RestorationTicketIdentifier,
        RestorationRequestorEmail,
        RestorationComment
    };

    public static readonly IReadOnlyList<string> AllKnownColumns =
        RequiredColumns
            .Concat(OptionalColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}