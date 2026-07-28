namespace RemediationTool.Domain.Entities;

/// <summary>
/// Canonical source-system configuration used to validate and enrich uploads.
/// Stored in the gfr-edg-source-systems DynamoDB table.
/// </summary>
public sealed class SourceSystemDefinition
{
    public string SourceSystem { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }

    public string? DataSystem { get; set; }

    public string? Description { get; set; }

    public string? DisplayName { get; set; }

    public bool IsEnabled { get; set; }

    public string? OriginatingDataSystem { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
