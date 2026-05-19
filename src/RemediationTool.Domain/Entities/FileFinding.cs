namespace RemediationTool.Domain;

public class FileFinding
{
    // Unique identifier for the finding
    public Guid Id { get; set; } = Guid.NewGuid();  
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string SourceSystem { get; set; }
    public long FileSize { get; set; }
    public string FindingType { get; set; }   // 🔥 REQUIRED
    public DateTime LastModifiedDate { get; set; }

    public FileStatus Status { get; set; } = FileStatus.Loaded;

    public string? QuarantinePath { get; set; }   // 🔥 REQUIRED
    public DateTime? UpdatedDate { get; set; }   // 🔥 REQUIRED
    
}