namespace RemediationTool.Domain.Entities;

public class RejectedRowDetail
{
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public int RowNumber { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string? RejectedValue { get; set; }

    public string ErrorReason { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}