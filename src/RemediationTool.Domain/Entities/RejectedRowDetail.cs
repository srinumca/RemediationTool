namespace RemediationTool.Domain.Entities;

public class RejectedRowDetail
{
    public string RejectedRowId { get; set; } = Guid.NewGuid().ToString("N");

    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public string? SourceRecordId { get; set; }

    public string? FindingFileName { get; set; }

    public string? FindingType { get; set; }

    public string? UserName { get; set; }

    public int RowNumber { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string? RejectedValue { get; set; }

    public string ErrorReason { get; set; } = string.Empty;

    public DateTime ErrorDateUtc { get; set; } = DateTime.UtcNow;

    public string? RawRowJson { get; set; }

    // Compatibility with existing JSON/repository code if it already uses CreatedAtUtc.
    public DateTime CreatedAtUtc
    {
        get => ErrorDateUtc;
        set => ErrorDateUtc = value;
    }
}