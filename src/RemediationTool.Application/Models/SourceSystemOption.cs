namespace RemediationTool.Application.Models;

/// <summary>
/// Source-system option returned to clients for upload selection controls.
/// </summary>
public sealed class SourceSystemOption
{
    public string SourceSystem { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? DataSystem { get; set; }

    public string? OriginatingDataSystem { get; set; }
}
