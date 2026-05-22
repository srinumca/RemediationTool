namespace RemediationTool.Domain.Entities;

public class FileFinding
{
    // 🔹 Existing (keep yours)
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string SourceSystem { get; set; }
    public long FileSize { get; set; }
    public string FindingType { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public FileStatus Status { get; set; }


    // Unique record identifier
    public Guid Id { get; set; }

    //Ingestion tracking
    public string IngestionId { get; set; }
    public string InboundFileName { get; set; }

    // Audit metadata
    public string UploadedBy { get; set; }
    public DateTime LoadDate { get; set; }
    public DateTime UpdatedDate { get; set; }

    // Validation
    public bool IsValid { get; set; }
    public string ErrorReason { get; set; }

    // Quarantine metadata
    public string QuarantinePath { get; set; }
}