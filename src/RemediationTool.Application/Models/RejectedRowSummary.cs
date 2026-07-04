using System.Text.Json.Serialization;

namespace RemediationTool.Application.Models;

public class RejectedRowSummary
{
    public string RejectedRowId { get; set; } = Guid.NewGuid().ToString("N");

    public string? SourceRecordId { get; set; }

    public string? FindingFileName { get; set; }

    public string? FindingType { get; set; }

    public string? UserName { get; set; }

    public int RowNumber { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string? RejectedValue { get; set; }

    public string ErrorReason { get; set; } = string.Empty;

    /// <summary>
    /// Error category classifying why this row was rejected.
    /// Recommended values are ValidationError, MissingRequiredField,
    /// InvalidAllowedValue, InvalidDataType, or MalformedInputRow.
    /// </summary>
    public string ErrorCategory { get; set; } = "ValidationError";

    public DateTime ErrorDateUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawRowJson { get; set; }
}
