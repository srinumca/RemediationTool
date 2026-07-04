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
        var counts = _repository.GetCountByFindingType();

        return new
        {
            Total = _repository.GetAll().Count,
            ByFindingType = counts,
            ByStatus = _repository.GetAll()
                .GroupBy(x => x.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
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