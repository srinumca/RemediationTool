using RemediationTool.Domain;

namespace RemediationTool.Application.Models;

/// <summary>
/// Data transfer object for file finding report views and dashboard queries.
/// FindingType is a plain string — no enum dependency.
/// Includes legacy properties so existing ReportService code compiles unchanged.
/// </summary>
public sealed class FileReportDto
{
    // ── Core fields ───────────────────────────────────────────────────────────
    public string FindingFileName { get; init; } = string.Empty;

    /// <summary>Plain string finding type from EDG report.</summary>
    public string FindingType { get; init; } = string.Empty;

    public string CurrentFileLocation { get; init; } = string.Empty;
    public string? OriginalFileLocation { get; init; }
    public DateTime? QuarantineDateUtc { get; init; }
    public DateTime? RestorationDateUtc { get; init; }
    public DateTime? DeletionDateUtc { get; init; }
    public string DataSystem { get; init; } = string.Empty;
    public string? SiteOwner { get; init; }
    public string? FileOwner { get; init; }

    // ── Workflow status ───────────────────────────────────────────────────────
    public FileStatus Status { get; init; }

    // ── Legacy properties — keeps ReportService compiling unchanged ───────────

    public string FileName
    {
        get => FindingFileName;
        init => FindingFileName = value ?? string.Empty;
    }

    public DateTime LastModifiedDate { get; init; }

    public string? QuarantinePath { get; init; }
}