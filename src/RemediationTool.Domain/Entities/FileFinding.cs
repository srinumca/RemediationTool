namespace RemediationTool.Domain.Entities;

public class FileFinding
{
    // Existing POC fields - keeping these for current Quarantine/Restore/Delete compatibility
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FindingType { get; set; } = string.Empty;
    public DateTime LastModifiedDate { get; set; }
    public FileStatus Status { get; set; }

    // Approved inbound layout fields
    public string FindingFileName { get; set; } = string.Empty;
    public string FindingFileFormat { get; set; } = string.Empty;
    public string CurrentFileLocation { get; set; } = string.Empty;
    public string OriginatingDataSystem { get; set; } = string.Empty;
    public string OriginatingVendorTool { get; set; } = string.Empty;
    public string? OriginalFileLocation { get; set; }
    public DateTime? QuarantineDate { get; set; }

    // Unique record identifier
    public Guid Id { get; set; }

    // Ingestion tracking
    public string IngestionId { get; set; } = string.Empty;
    public string InboundFileName { get; set; } = string.Empty;

    // Audit metadata
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime LoadDate { get; set; }
    public DateTime UpdatedDate { get; set; }

    // Validation
    public bool IsValid { get; set; }
    public string ErrorReason { get; set; } = string.Empty;

    // Quarantine metadata
    public string QuarantinePath { get; set; } = string.Empty;
}