using System.Globalization;
using System.Text;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

/// <summary>
/// Report service — queries findings for dashboard and reporting.
/// All FindingType comparisons use plain strings.
/// </summary>
public class ReportService
{
    private readonly IFileFindingRepository _repository;

    public ReportService(IFileFindingRepository repository)
    {
        _repository = repository;
    }

    public List<FileReportDto> GetAll()
    {
        return _repository.GetAll()
            .Select(ToDto)
            .ToList();
    }

    public List<FileReportDto> GetByStatus(string status)
    {
        if (!Enum.TryParse<FileStatus>(status, ignoreCase: true, out var parsedStatus))
            return new List<FileReportDto>();

        return _repository.GetAll()
            .Where(x => x.Status == parsedStatus)
            .Select(ToDto)
            .ToList();
    }

    public List<FileReportDto> GetByFindingType(string findingType)
    {
        return _repository.GetLatestByFindingType(findingType)
            .Select(ToDto)
            .ToList();
    }

    public object GetSummary()
    {
        var findings = _repository.GetAll();
        var counts = findings
            .GroupBy(x => x.FindingType ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new
        {
            Total = findings.Count,
            ByFindingType = counts,
            ByStatus = findings
                .GroupBy(x => x.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public DashboardSummaryDto GetDashboardSummary()
    {
        var findings = _repository.GetAll();
        var byStatus = findings
            .GroupBy(x => x.Status.ToString())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var byFindingType = findings
            .GroupBy(x => x.FindingType ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var byTab = findings
            .GroupBy(ResolveWorkflowTab)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        byTab.TryGetValue("NotYetStarted", out var notYetStarted);
        byTab.TryGetValue("InProgress", out var inProgress);
        byTab.TryGetValue("Errors", out var errors);
        byTab.TryGetValue("Exceptions", out var exceptions);
        byTab.TryGetValue("Quarantined", out var quarantined);
        byTab.TryGetValue("Restoration", out var restoration);
        byTab.TryGetValue("Deleted", out var deleted);

        return new DashboardSummaryDto
        {
            Total = findings.Count,
            ByStatus = byStatus,
            ByFindingType = byFindingType,
            ByTab = byTab,
            PendingActionCount = notYetStarted + inProgress,
            ErrorCount = errors,
            ExceptionCount = exceptions,
            QuarantinedCount = quarantined,
            RestorationCount = restoration,
            DeletedCount = deleted
        };
    }

    public List<FileReportDto> GetByWorkflowTab(string tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
            return new List<FileReportDto>();

        var normalizedTab = NormalizeTab(tab);

        return _repository.GetAll()
            .Where(x => string.Equals(ResolveWorkflowTab(x), normalizedTab, StringComparison.OrdinalIgnoreCase))
            .Select(ToDto)
            .ToList();
    }

    public byte[] ExportCsv(string? tab = null, string? status = null, string? findingType = null)
    {
        IEnumerable<FileFinding> query = _repository.GetAll();

        if (!string.IsNullOrWhiteSpace(tab))
        {
            var normalizedTab = NormalizeTab(tab);
            query = query.Where(x => string.Equals(ResolveWorkflowTab(x), normalizedTab, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<FileStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(findingType))
            query = query.Where(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase));

        var rows = query.Select(ToDto).ToList();
        var csv = new StringBuilder();
        csv.AppendLine("FileName,FindingType,Status,CurrentFileLocation,OriginalFileLocation,QuarantineDateUtc,RestorationDateUtc,DeletionDateUtc,DataSystem,SiteOwner,FileOwner,LastModifiedDate,QuarantinePath");

        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                Escape(row.FileName),
                Escape(row.FindingType),
                Escape(row.Status.ToString()),
                Escape(row.CurrentFileLocation),
                Escape(row.OriginalFileLocation),
                Escape(row.QuarantineDateUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Escape(row.RestorationDateUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Escape(row.DeletionDateUtc?.ToString("O", CultureInfo.InvariantCulture)),
                Escape(row.DataSystem),
                Escape(row.SiteOwner),
                Escape(row.FileOwner),
                Escape(row.LastModifiedDate == DateTime.MinValue ? string.Empty : row.LastModifiedDate.ToString("O", CultureInfo.InvariantCulture)),
                Escape(row.QuarantinePath)
            }));
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string ResolveWorkflowTab(FileFinding finding)
    {
        return finding.Status switch
        {
            FileStatus.NotYetStarted => "NotYetStarted",
            FileStatus.PendingQuarantine => "InProgress",
            FileStatus.PendingRestore => "InProgress",
            FileStatus.InProgress => "InProgress",
            FileStatus.DeletionInProgress => "InProgress",
            FileStatus.QuarantineComplete => "Quarantined",
            FileStatus.Restoration => "Restoration",
            FileStatus.RestorationComplete => "Restoration",
            FileStatus.Exception => "Exceptions",
            FileStatus.Exclusion => "Exceptions",
            FileStatus.Error => "Errors",
            FileStatus.DeletionComplete => "Deleted",
            FileStatus.Deleted => "Deleted",
            FileStatus.Quarantined => "Quarantined",
            FileStatus.Restored => "Restoration",
            _ => "Other"
        };
    }

    private static string NormalizeTab(string tab)
    {
        var normalized = tab.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);

        return normalized.ToLowerInvariant() switch
        {
            "notyetstarted" => "NotYetStarted",
            "inprogress" => "InProgress",
            "quarantined" => "Quarantined",
            "restoration" => "Restoration",
            "restore" => "Restoration",
            "exceptions" => "Exceptions",
            "exception" => "Exceptions",
            "errors" => "Errors",
            "error" => "Errors",
            "deleted" => "Deleted",
            _ => normalized
        };
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var escaped = value.Replace("\"", "\"\"");
        return escaped.Contains(',') || escaped.Contains('\n') || escaped.Contains('\r') || escaped.Contains('"')
            ? $"\"{escaped}\""
            : escaped;
    }

    private static FileReportDto ToDto(FileFinding x) => new()
    {
        FindingFileName = x.FindingFileName,
        FindingType = x.FindingType,
        CurrentFileLocation = x.CurrentFileLocation,
        OriginalFileLocation = x.OriginalFileLocation,
        QuarantineDateUtc = x.QuarantineDateUtc,
        RestorationDateUtc = x.RestoredDateUtc,
        DeletionDateUtc = x.DeletedDateUtc,
        DataSystem = x.OriginatingDataSystem,
        SiteOwner = x.SiteOwner,
        FileOwner = x.FileOwner,
        Status = x.Status,
        LastModifiedDate = x.LastModifiedDate,
        QuarantinePath = x.QuarantinePath,
        FileName = x.FileName,
    };
}
