using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Services;

public class ReportService
{
    private readonly IFileFindingRepository _repository;

    public ReportService(IFileFindingRepository repository)
    {
        _repository = repository;
    }

    public IReadOnlyList<FileReportDto> GetByFindingType(FindingType findingType)
    {
        return _repository
            .GetLatestByFindingType(findingType)
            .Select(x => new FileReportDto
            {
                FindingFileName = x.FindingFileName,
                FindingType = x.FindingType,
                CurrentFileLocation = x.CurrentFileLocation,
                OriginalFileLocation = x.OriginalFileLocation,
                LastModifiedDateUtc = x.LastModifiedDateUtc,
                QuarantineDateUtc = x.QuarantineDateUtc,
                RestorationDateUtc = x.RestorationDateUtc,
                DeletionDateUtc = x.DeletionDateUtc,
                DataSystem = x.DataSystem,
                SiteOwner = x.SiteOwner,
                FileOwner = x.FileOwner,
                ErrorCategory = x.ErrorCategory
            })
            .ToList();
    }

    public IReadOnlyDictionary<FindingType, int> GetSummaryByFindingType()
    {
        return _repository.GetCountByFindingType();
    }

    public IReadOnlyList<FileReportDto> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        return _repository
            .GetHistoryBySourceRecordId(sourceRecordId)
            .Select(x => new FileReportDto
            {
                FindingFileName = x.FindingFileName,
                FindingType = x.FindingType,
                CurrentFileLocation = x.CurrentFileLocation,
                OriginalFileLocation = x.OriginalFileLocation,
                LastModifiedDateUtc = x.LastModifiedDateUtc,
                QuarantineDateUtc = x.QuarantineDateUtc,
                RestorationDateUtc = x.RestorationDateUtc,
                DeletionDateUtc = x.DeletionDateUtc,
                DataSystem = x.DataSystem,
                SiteOwner = x.SiteOwner,
                FileOwner = x.FileOwner,
                ErrorCategory = x.ErrorCategory
            })
            .ToList();
    }
}