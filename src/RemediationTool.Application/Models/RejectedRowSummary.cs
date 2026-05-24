namespace RemediationTool.Application.Models;

public class RejectedRowSummary
{
    public int RowNumber { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string? RejectedValue { get; set; }

    public string ErrorReason { get; set; } = string.Empty;
}