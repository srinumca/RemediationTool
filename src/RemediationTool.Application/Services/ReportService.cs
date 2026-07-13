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
    private static readonly IReadOnlyDictionary<string, string> WorkflowTabAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notyetstarted"] = "NotYetStarted",
            ["inprogress"] = "InProgress",
            ["quarantined"] = "Quarantined",
            ["restoration"] = "Restoration",
            ["restore"] = "Restoration",
            ["exceptions"] = "Exceptions",
            ["exception"] = "Exceptions",
            ["errors"] = "Errors",
            ["error"] = "Errors",
            ["deleted"] = "Deleted"
        };

    private readonly IFileFindingRepository _repository;

    public ReportService(IFileFindingRepository repository)
    {
        _repository = repository;
    }

    public List<FileReportDto> GetAll()
    {
        var findings = _repository.GetAll();
        var result = new List<FileReportDto>(findings.Count);

        foreach (var finding in findings)
            result.Add(ToDto(finding));

        return result;
    }

    public List<FileReportDto> GetByStatus(string status)
    {
        if (!Enum.TryParse<FileStatus>(status, ignoreCase: true, out var parsedStatus))
            return new List<FileReportDto>();

        var findings = _repository.GetAll();
        var result = new List<FileReportDto>();

        foreach (var finding in findings)
        {
            if (finding.Status == parsedStatus)
                result.Add(ToDto(finding));
        }

        return result;
    }

    public List<FileReportDto> GetByFindingType(string findingType)
    {
        var findings = _repository.GetLatestByFindingType(findingType);
        var result = new List<FileReportDto>(findings.Count);

        foreach (var finding in findings)
            result.Add(ToDto(finding));

        return result;
    }

    public object GetSummary()
    {
        var findings = _repository.GetAll();
        var byFindingType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            Increment(byFindingType, finding.FindingType ?? string.Empty);
            Increment(byStatus, finding.Status.ToString());
        }

        return new
        {
            Total = findings.Count,
            ByFindingType = byFindingType,
            ByStatus = byStatus
        };
    }

    public DashboardSummaryDto GetDashboardSummary()
    {
        var findings = _repository.GetAll();
        var byStatus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byFindingType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byTab = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in findings)
        {
            Increment(byStatus, finding.Status.ToString());
            Increment(byFindingType, finding.FindingType ?? string.Empty);
            Increment(byTab, ResolveWorkflowTab(finding));
        }

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
        var findings = _repository.GetAll();
        var result = new List<FileReportDto>();

        foreach (var finding in findings)
        {
            if (string.Equals(
                    ResolveWorkflowTab(finding),
                    normalizedTab,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ToDto(finding));
            }
        }

        return result;
    }

    public byte[] ExportCsv(string? tab = null, string? status = null, string? findingType = null)
    {
        var findings = _repository.GetAll();
        var normalizedTab = string.IsNullOrWhiteSpace(tab) ? null : NormalizeTab(tab);
        var parsedStatus = default(FileStatus);
        var hasStatusFilter = !string.IsNullOrWhiteSpace(status)
                              && Enum.TryParse(status, ignoreCase: true, out parsedStatus);

        var estimatedCapacity = (int)Math.Min(
            1_048_576L,
            Math.Max(1024L, (long)findings.Count * 128L));
        var csv = new StringBuilder(estimatedCapacity);
        csv.AppendLine("FileName,FindingType,Status,CurrentFileLocation,OriginalFileLocation,QuarantineDateUtc,RestorationDateUtc,DeletionDateUtc,DataSystem,SiteOwner,FileOwner,LastModifiedDate,QuarantinePath");

        foreach (var finding in findings)
        {
            if (normalizedTab != null
                && !string.Equals(
                    ResolveWorkflowTab(finding),
                    normalizedTab,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (hasStatusFilter && finding.Status != parsedStatus)
                continue;

            if (!string.IsNullOrWhiteSpace(findingType)
                && !string.Equals(finding.FindingType, findingType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AppendCsvValue(csv, finding.FileName);
            csv.Append(',');
            AppendCsvValue(csv, finding.FindingType);
            csv.Append(',');
            AppendCsvValue(csv, finding.Status.ToString());
            csv.Append(',');
            AppendCsvValue(csv, finding.CurrentFileLocation);
            csv.Append(',');
            AppendCsvValue(csv, finding.OriginalFileLocation);
            csv.Append(',');
            AppendCsvValue(csv, finding.QuarantineDateUtc?.ToString("O", CultureInfo.InvariantCulture));
            csv.Append(',');
            AppendCsvValue(csv, finding.RestoredDateUtc?.ToString("O", CultureInfo.InvariantCulture));
            csv.Append(',');
            AppendCsvValue(csv, finding.DeletedDateUtc?.ToString("O", CultureInfo.InvariantCulture));
            csv.Append(',');
            AppendCsvValue(csv, finding.OriginatingDataSystem);
            csv.Append(',');
            AppendCsvValue(csv, finding.SiteOwner);
            csv.Append(',');
            AppendCsvValue(csv, finding.FileOwner);
            csv.Append(',');
            AppendCsvValue(
                csv,
                finding.LastModifiedDate == DateTime.MinValue
                    ? string.Empty
                    : finding.LastModifiedDate.ToString("O", CultureInfo.InvariantCulture));
            csv.Append(',');
            AppendCsvValue(csv, finding.QuarantinePath);
            csv.AppendLine();
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        if (counts.TryGetValue(key, out var currentCount))
            counts[key] = currentCount + 1;
        else
            counts[key] = 1;
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
        Span<char> buffer = tab.Length <= 256
            ? stackalloc char[tab.Length]
            : new char[tab.Length];

        var length = 0;
        foreach (var character in tab.Trim())
        {
            if (character is ' ' or '-' or '_')
                continue;

            buffer[length++] = character;
        }

        var normalized = new string(buffer[..length]);
        return WorkflowTabAliases.TryGetValue(normalized, out var mappedTab)
            ? mappedTab
            : normalized;
    }

    private static void AppendCsvValue(StringBuilder csv, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var requiresQuotes = false;
        foreach (var character in value)
        {
            if (character is ',' or '\n' or '\r' or '"')
            {
                requiresQuotes = true;
                break;
            }
        }

        if (requiresQuotes)
            csv.Append('"');

        foreach (var character in value)
        {
            if (character == '"')
                csv.Append("\"\"");
            else
                csv.Append(character);
        }

        if (requiresQuotes)
            csv.Append('"');
    }

    private static FileReportDto ToDto(FileFinding finding) => new()
    {
        FindingFileName = finding.FindingFileName,
        FindingType = finding.FindingType,
        CurrentFileLocation = finding.CurrentFileLocation,
        OriginalFileLocation = finding.OriginalFileLocation,
        QuarantineDateUtc = finding.QuarantineDateUtc,
        RestorationDateUtc = finding.RestoredDateUtc,
        DeletionDateUtc = finding.DeletedDateUtc,
        DataSystem = finding.OriginatingDataSystem,
        SiteOwner = finding.SiteOwner,
        FileOwner = finding.FileOwner,
        Status = finding.Status,
        LastModifiedDate = finding.LastModifiedDate,
        QuarantinePath = finding.QuarantinePath,
        FileName = finding.FileName
    };
}
