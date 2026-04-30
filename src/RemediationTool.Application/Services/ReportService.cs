using RemediationTool.Application.Models;

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
        try
        {
            return _repository.GetAll()
                .Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
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

    // Summary
    public object GetSummary()
    {
        try
        {
            var data = _repository.GetAll();

            return new
            {
                Total = data.Count,
                Loaded = data.Count(x => x.Status == "Loaded"),
                Quarantined = data.Count(x => x.Status == "Quarantined"),
                Restored = data.Count(x => x.Status == "Restored")
            };
        }
        catch
        {
            throw;
        }
    }
}