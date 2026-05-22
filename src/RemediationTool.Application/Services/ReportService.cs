using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

public class ReportService
{
    private readonly IFileFindingRepository _repository;

    public ReportService(IFileFindingRepository repository)
    {
        _repository = repository;
    }

    // Get all
    public List<FileReportDto> GetAll()
    {
        try
        {
            return _repository.GetAll()
                .Select(x => new FileReportDto
                {
                    FileName = x.FileName,
                    Status = x.Status,
                    LastModifiedDate = x.LastModifiedDate,
                    QuarantinePath = x.QuarantinePath
                })
                .ToList();
        }
        catch
        {
            throw;
        }
    }

    // Filter by status
    public List<FileReportDto> GetByStatus(string status)
    {
        // 🔥 Convert string → enum
        if (!Enum.TryParse<FileStatus>(status, true, out var parsedStatus))
        {
            return new List<FileReportDto>();
        }

        return _repository.GetAll()
            .Where(x => x.Status == parsedStatus)
            .Select(x => new FileReportDto
            {
                FileName = x.FileName,
                Status = x.Status,
            })
            .ToList();
    }

    // Summary
    public object GetSummary()
    {
        try
        {
            var data = _repository.GetAll();

            return new
            {
                Total = data.Count,
                Loaded = data.Count(x => x.Status == FileStatus.Loaded),
                Quarantined = data.Count(x => x.Status == FileStatus.Quarantined),
                Restored = data.Count(x => x.Status == FileStatus.Restored)
            };
        }
        catch
        {
            throw;
        }
    }

    
}