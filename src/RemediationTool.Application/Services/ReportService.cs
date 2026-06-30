using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ReportService> _logger;

    public ReportService(IFileFindingRepository repository, ILogger<ReportService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Gets all file findings and maps them to FileReportDto objects.
    /// </summary>
    /// <returns></returns>
    public List<FileReportDto> GetAll()
    {
        _logger.LogDebug("ReportService.GetAll invoked");
        try
        {
            var results = _repository.GetAll()
                .Select(ToDto)
                .ToList();

            _logger.LogInformation("GetAll report retrieved. TotalRecords: {TotalRecords}", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all records in GetAll report");
            throw;
        }
    }

    /// <summary>
    /// Gets file findings filtered by status and maps them to FileReportDto objects.
    /// </summary>
    /// <param name="status"></param>
    /// <returns></returns>
    public List<FileReportDto> GetByStatus(string status)
    {
        _logger.LogDebug("ReportService.GetByStatus invoked with status: {Status}", status);

        if (!Enum.TryParse<FileStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            _logger.LogWarning("Invalid status provided to GetByStatus: {Status}", status);
            return new List<FileReportDto>();
        }

        try
        {
            var results = _repository.GetAll()
                .Where(x => x.Status == parsedStatus)
                .Select(ToDto)
                .ToList();

            _logger.LogInformation("GetByStatus report retrieved. Status: {Status}, RecordCount: {RecordCount}", parsedStatus, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving records by status in GetByStatus report. Status: {Status}", status);
            throw;
        }
    }

    public List<FileReportDto> GetByFindingType(string findingType)
    {
        _logger.LogDebug("ReportService.GetByFindingType invoked with findingType: {FindingType}", findingType);

        try
        {
            var results = _repository.GetLatestByFindingType(findingType)
                .Select(ToDto)
                .ToList();

            _logger.LogInformation("GetByFindingType report retrieved. FindingType: {FindingType}, RecordCount: {RecordCount}", findingType, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving records by finding type in GetByFindingType report. FindingType: {FindingType}", findingType);
            throw;
        }
    }

    public object GetSummary()
    {
        _logger.LogDebug("ReportService.GetSummary invoked");

        try
        {
            var counts = _repository.GetCountByFindingType();
            var allRecords = _repository.GetAll();
            var totalCount = allRecords.Count;
            var statusCounts = allRecords
                .GroupBy(x => x.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("GetSummary report generated. TotalRecords: {TotalRecords}, FindingTypeCount: {FindingTypeCount}, StatusGroupCount: {StatusGroupCount}",
                totalCount, counts?.Count ?? 0, statusCounts?.Count ?? 0);

            return new
            {
                Total = totalCount,
                ByFindingType = counts,
                ByStatus = statusCounts
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary report in GetSummary");
            throw;
        }
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