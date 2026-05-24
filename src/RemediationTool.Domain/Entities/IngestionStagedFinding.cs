namespace RemediationTool.Domain.Entities;

public class IngestionStagedFinding
{
    public string JobId { get; set; } = string.Empty;

    public int SequenceNumber { get; set; }

    public FileFinding Finding { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}