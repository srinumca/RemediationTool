namespace RemediationTool.Application.Constants;

public static class InboundLayoutColumns
{
    public const string FindingFileName = "Finding_File_Name";
    public const string FindingFileFormat = "Finding File Format";
    public const string CurrentFileLocation = "Current_File_Location";
    public const string FindingType = "Finding_Type";
    public const string OriginatingDataSystem = "Originating_Data_System";
    public const string OriginatingVendorTool = "Originating_Vendor_Tool";
    public const string OriginalFileLocation = "Original_File_Location";
    public const string QuarantineDate = "Quarantine_Date";

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
        OriginalFileLocation,
        QuarantineDate
    };
}