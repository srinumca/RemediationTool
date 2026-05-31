using RemediationTool.Domain.Enums;

namespace RemediationTool.Domain.Entities;

/// <summary>
/// Core domain entity representing a single file finding in the remediation data model.
/// Field coverage is aligned 1:1 with the Data Model tab in the requirements specification.
///
/// Design principles:
///   - Id is always system-generated. Never derived from inbound data.
///   - SourceRecordId is a separate lineage field for traceability to the upstream source.
///   - FindingType uses the strongly-typed FindingType enum (stored as string via JsonStringEnumConverter).
///   - All date fields are UTC and nullable where the spec defines them as optional.
///   - IsValid / IngestionErrorReason are ingestion-pipeline-only fields used during batch
///     processing. They are NOT part of the spec Data Model and are NOT persisted to DynamoDB.
///     They exist only to carry per-row validation state through the ingestion pipeline.
/// </summary>
public class FileFinding
{
    // =========================================================================
    // SYSTEM-GENERATED FIELDS (set by the tool, never from inbound data)
    // =========================================================================

    /// <summary>
    /// Internal unique identifier for this record version. Always system-generated.
    /// Never sourced from inbound data. Maps to: ID (Data Model — system generated).
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique version identifier for this specific row. Supports the append-only
    /// (insert new row per state change) persistence pattern.
    /// </summary>
    public string RecordVersionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The ID value from the inbound source file ("ID" column). Stored for lineage
    /// and traceability back to the upstream system. Never used as the internal primary key.
    /// </summary>
    public string? SourceRecordId { get; set; }

    /// <summary>The JobId of the ingestion job that loaded this record.</summary>
    public string? IngestionJobId { get; set; }

    /// <summary>
    /// Name of the inbound file that contained this record.
    /// Maps to: Inbound_File_Name (Data Model — system generated).
    /// </summary>
    public string InboundFileName { get; set; } = string.Empty;

    /// <summary>
    /// UserID of the associate who initiated the ingestion, or "System" for automated jobs.
    /// Maps to: UserName (Data Model — system generated).
    /// </summary>
    public string UserName { get; set; } = "System";

    /// <summary>
    /// UTC timestamp when this record was first loaded into the remediation tool.
    /// Maps to: Load_Date (Data Model — system generated).
    /// </summary>
    public DateTime LoadDateUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent update to this record.
    /// Set to LoadDateUtc on initial load; updated on every state change.
    /// Maps to: Last_Update_Date (Data Model — system generated).
    /// </summary>
    public DateTime LastUpdateDateUtc { get; set; } = DateTime.UtcNow;

    // =========================================================================
    // INBOUND FILE FIELDS (sourced from the inbound CSV/XLSX payload)
    // =========================================================================

    /// <summary>Name of the file (including extension) identified as out of compliance. Maps to: Finding_File_Name.</summary>
    public string FindingFileName { get; set; } = string.Empty;

    /// <summary>Format/extension of the file (e.g. doc, xls, pdf). Maps to: Finding File Format.</summary>
    public string FindingFileFormat { get; set; } = string.Empty;

    /// <summary>File size in bytes. Null if not provided. Maps to: Finding File Size.</summary>
    public long? FindingFileSizeBytes { get; set; }

    /// <summary>
    /// The file path where the file is currently located.
    /// For Quarantined files this holds the quarantine path.
    /// For Restored files this holds the restored path.
    /// Maps to: Current_File_Location.
    /// </summary>
    public string CurrentFileLocation { get; set; } = string.Empty;

    /// <summary>
    /// The lifecycle state of this file within the remediation workflow.
    /// Allowed values: Obsolete, Quarantined, Restored, Deleted, NotObsolete, Exclusion.
    /// Maps to: Finding_Type (Data Model).
    /// </summary>
    public FindingType FindingType { get; set; }

    /// <summary>
    /// The specific NetApp drive path including parent folder
    /// (e.g. dc1f7c2nasv3.es.ad.adp.com/enterprise).
    /// More granular than OriginatingDataSystem — identifies the exact drive.
    /// Maps to: Data_System (Inbound File Layout — mandatory).
    /// </summary>
    public string DataSystem { get; set; } = string.Empty;

    /// <summary>Source data system type (e.g. SMB, NFS, M365). Maps to: Originating_Data_System.</summary>
    public string OriginatingDataSystem { get; set; } = string.Empty;

    /// <summary>The tool that originally detected this finding (e.g. Securiti). Maps to: Originating_Vendor_Tool.</summary>
    public string OriginatingVendorTool { get; set; } = string.Empty;

    /// <summary>
    /// The original SMB/NFS file path before quarantine.
    /// Only populated for Quarantined files. Required when FindingType = Quarantined.
    /// Maps to: Original_File_Location.
    /// </summary>
    public string? OriginalFileLocation { get; set; }

    /// <summary>UTC date the file was quarantined. Required when FindingType = Quarantined. Maps to: Quarantine_Date.</summary>
    public DateTime? QuarantineDateUtc { get; set; }

    /// <summary>UTC date the file was restored from quarantine. Maps to: Restoration_Date.</summary>
    public DateTime? RestorationDateUtc { get; set; }

    /// <summary>
    /// UTC date the file was marked as Exclusion via the UI exclusion workflow.
    /// Stamped when FindingType is set to Exclusion.
    /// Maps to: Exception_Date (Data Model).
    /// </summary>
    public DateTime? ExceptionDateUtc { get; set; }

    /// <summary>UTC date the file was permanently deleted. Maps to: Deletion_Date.</summary>
    public DateTime? DeletionDateUtc { get; set; }

    /// <summary>UTC timestamp of the file's last modification at source. Used for obsolescence check (10+ years).</summary>
    public DateTime? LastModifiedDateUtc { get; set; }

    /// <summary>UTC timestamp of when the file was originally created.</summary>
    public DateTime? CreatedDateUtc { get; set; }

    /// <summary>UTC timestamp of when the file was last accessed.</summary>
    public DateTime? LastAccessedDateUtc { get; set; }

    /// <summary>Associate name of the site or drive owner. Maps to: Site_Owner.</summary>
    public string? SiteOwner { get; set; }

    /// <summary>Associate name of the file owner. Maps to: File_Owner.</summary>
    public string? FileOwner { get; set; }

    /// <summary>Business unit associated with the file owner or data system.</summary>
    public string? BusinessUnit { get; set; }

    /// <summary>Division associated with the file owner or data system.</summary>
    public string? Division { get; set; }

    /// <summary>Department associated with the file owner or data system.</summary>
    public string? Department { get; set; }

    /// <summary>Geographic region of the data system (e.g. US, EMEA, APAC).</summary>
    public string? Region { get; set; }

    /// <summary>Country of the data system.</summary>
    public string? Country { get; set; }

    /// <summary>Name of the data governance policy that flagged this file.</summary>
    public string? PolicyName { get; set; }

    /// <summary>Identifier of the data governance policy.</summary>
    public string? PolicyId { get; set; }

    /// <summary>Human-readable reason why this file was flagged as a finding.</summary>
    public string? FindingReason { get; set; }

    /// <summary>Risk level assigned by the source scanning tool (Low, Medium, High, Critical).</summary>
    public string? RiskLevel { get; set; }

    /// <summary>Sensitivity label applied to the file (e.g. Confidential, Internal).</summary>
    public string? SensitivityLabel { get; set; }

    /// <summary>UTC date the finding was detected by the source scanning tool.</summary>
    public DateTime? DetectionDateUtc { get; set; }

    /// <summary>Recommended remediation action as suggested by the source scanning tool.</summary>
    public string? RecommendedAction { get; set; }

    // =========================================================================
    // RESTORATION WORKFLOW FIELDS
    // =========================================================================

    /// <summary>
    /// Ticket number/reference ID from the file restoration request (e.g. Jira ticket).
    /// Maps to: Restoration Ticket Identifier (Data Model).
    /// </summary>
    public string? RestorationTicketIdentifier { get; set; }

    /// <summary>
    /// ADP email address of the associate who requested restoration.
    /// Maps to: Restoration Requestor Email (Data Model).
    /// </summary>
    public string? RestorationRequestorEmail { get; set; }

    /// <summary>
    /// Free-form comment captured at the time of the restoration request (optional).
    /// Maps to: Restoration_Comment (Data Model).
    /// </summary>
    public string? RestorationComment { get; set; }

    // =========================================================================
    // ERROR TRACKING (operational errors during remediation actions)
    // =========================================================================

    /// <summary>
    /// Standardised error category describing why a remediation action failed.
    /// Maps to: Error Category (Data Model tab). Uses the ErrorCategory taxonomy
    /// from the Error Categories specification tab.
    /// Only set when quarantine/restore/delete fails — not for ingestion validation failures.
    /// </summary>
    public ErrorCategory ErrorCategory { get; set; } = ErrorCategory.None;

    /// <summary>
    /// Free-form error detail message supplementing ErrorCategory.
    /// Captures the specific exception message or system error description.
    /// </summary>
    public string? ErrorDetail { get; set; }

    // =========================================================================
    // INGESTION PIPELINE FIELDS (internal use only — NOT persisted to DynamoDB)
    // These fields carry per-row state through the ingestion pipeline only.
    // They are not part of the spec Data Model and must be excluded from
    // DynamoDB persistence when that layer is implemented.
    // =========================================================================

    /// <summary>
    /// True when this record passed all validation rules during ingestion.
    /// Used internally by the ingestion pipeline to separate valid from rejected records.
    /// Not persisted to DynamoDB.
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Validation error message(s) for rejected records during ingestion.
    /// Not persisted to DynamoDB — rejection detail is stored in RejectedRowDetail instead.
    /// </summary>
    public string IngestionErrorReason { get; set; } = string.Empty;
}