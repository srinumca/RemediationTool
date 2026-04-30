namespace RemediationTool.Domain;

public class FileFinding
{
    // Unique identifier for the finding
    public string Id { get; set; }

    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string SourceSystem { get; set; }
    public long FileSize { get; set; }
    public string FindingType { get; set; }   // 🔥 REQUIRED
    public DateTime LastModifiedDate { get; set; }

    public string Status { get; set; } = "Loaded";

    public string? QuarantinePath { get; set; }   // 🔥 REQUIRED
}