using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Models;

/// <summary>
/// Data transfer object for file finding report views and dashboard queries.
/// Contains only the fields needed for reporting — not the full FileFinding entity.
/// All property names use canonical names from the Data Model specification.
/// </summary>
public sealed class FileReportDto
{
    /// <summary>Name of the file (including extension).</summary>
    public string FindingFileName { get; init; } = string.Empty;

    /// <summary>Current lifecycle state of this finding.</summary>
    public FindingType FindingType { get; init; }

    /// <summary>Current file path (quarantine path for Quarantined, restored path for Restored).</summary>
    public string CurrentFileLocation { get; init; } = string.Empty;

    /// <summary>Original SMB/NFS path before quarantine. Null for non-quarantined records.</summary>
    public string? OriginalFileLocation { get; init; }

    /// <summary>UTC date the file was quarantined. Null if not yet quarantined.</summary>
    public DateTime? QuarantineDateUtc { get; init; }

    /// <summary>UTC date the file was restored. Null if not restored.</summary>
    public DateTime? RestorationDateUtc { get; init; }

    /// <summary>UTC date the file was permanently deleted. Null if not deleted.</summary>
    public DateTime? DeletionDateUtc { get; init; }

    /// <summary>
    /// Specific NetApp drive path (e.g. dc1f7c2nasv3.es.ad.adp.com/enterprise).
    /// Used for filtering and breakdown by data system in dashboards.
    /// </summary>
    public string DataSystem { get; init; } = string.Empty;

    /// <summary>Associate name of the site or drive owner.</summary>
    public string? SiteOwner { get; init; }

    /// <summary>Associate name of the file owner.</summary>
    public string? FileOwner { get; init; }

    /// <summary>
    /// Error category if the most recent remediation action failed.
    /// None means the action succeeded.
    /// </summary>
    public ErrorCategory ErrorCategory { get; init; } = ErrorCategory.None;
}