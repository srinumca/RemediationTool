using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Adds Parquet working-file behavior around the existing staging repository.
/// Save still delegates to staging, but also creates the Parquet working file.
/// Resume reads from Parquet first, then falls back to staging if needed.
/// </summary>
public sealed class ParquetAwareIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly IIngestionStagingRepository _inner;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;
    private readonly IngestionProcessingOptions _options;
    private readonly ILogger<ParquetAwareIngestionStagingRepository> _logger;

    public ParquetAwareIngestionStagingRepository(
        IIngestionStagingRepository inner,
        IIngestionJobAuditRepository jobAuditRepository,
        IIngestionWorkingFileStrategy workingFileStrategy,
        IOptions<IngestionProcessingOptions> options,
        ILogger<ParquetAwareIngestionStagingRepository> logger)
    {
        _inner = inner;
        _jobAuditRepository = jobAuditRepository;
        _workingFileStrategy = workingFileStrategy;
        _options = options.Value;
        _logger = logger;
    }

    public void SaveValidFindings(string jobId, IReadOnlyList<FileFinding> validFindings)
    {
        _inner.SaveValidFindings(jobId, validFindings);

        if (!_options.EnableParquetWorkingFile || validFindings.Count == 0)
            return;

        var jobAudit = _jobAuditRepository.GetByJobId(jobId);
        if (jobAudit == null)
        {
            _logger.LogWarning(
                "[PARQUET_WRITE_SKIPPED] JobId:{JobId}, Reason:Job audit record not found.",
                jobId);
            return;
        }

        _logger.LogInformation(
            "[PARQUET_STAGING_WRITE_START] JobId:{JobId}, InboundFileName:{InboundFileName}, RecordCount:{RecordCount}",
            jobId, jobAudit.InboundFileName, validFindings.Count);

        var workingFileResult = _workingFileStrategy
            .WriteAsync(jobId, jobAudit.InboundFileName, validFindings)
            .GetAwaiter()
            .GetResult();

        jobAudit.WorkingFileFormat = workingFileResult.Format;
        jobAudit.WorkingFilePath = workingFileResult.Path;
        jobAudit.WorkingFileRecordCount = workingFileResult.RecordCount;
        _jobAuditRepository.Update(jobAudit);

        _logger.LogInformation(
            "[PARQUET_STAGING_WRITE_COMPLETE] JobId:{JobId}, WorkingFilePath:{WorkingFilePath}, Format:{Format}, RecordCount:{RecordCount}",
            jobId, workingFileResult.Path, workingFileResult.Format, workingFileResult.RecordCount);
    }

    public List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount)
    {
        if (_options.EnableParquetWorkingFile)
        {
            var jobAudit = _jobAuditRepository.GetByJobId(jobId);
            if (IsParquetAvailable(jobAudit))
            {
                try
                {
                    _logger.LogInformation(
                        "[PARQUET_RESUME_READ_ATTEMPT] JobId:{JobId}, WorkingFilePath:{WorkingFilePath}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                        jobId, jobAudit!.WorkingFilePath, lastProcessedRecordCount);

                    var records = _workingFileStrategy
                        .ReadAfterAsync(jobAudit.WorkingFilePath!, lastProcessedRecordCount)
                        .GetAwaiter()
                        .GetResult();

                    if (records.Count > 0 || lastProcessedRecordCount >= jobAudit.WorkingFileRecordCount)
                    {
                        _logger.LogInformation(
                            "[PARQUET_RESUME_READ_SUCCESS] JobId:{JobId}, RemainingRecordCount:{RemainingRecordCount}",
                            jobId, records.Count);
                        return records;
                    }

                    _logger.LogWarning(
                        "[PARQUET_RESUME_EMPTY_FALLBACK] JobId:{JobId}, WorkingFilePath:{WorkingFilePath}. Falling back to staging.",
                        jobId, jobAudit.WorkingFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[PARQUET_RESUME_READ_FAILED] JobId:{JobId}. Falling back to staging.",
                        jobId);
                }
            }
        }

        _logger.LogInformation(
            "[STAGING_RESUME_READ] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId, lastProcessedRecordCount);

        return _inner.GetValidFindingsAfter(jobId, lastProcessedRecordCount);
    }

    public int CountByJobId(string jobId)
    {
        var stagingCount = _inner.CountByJobId(jobId);
        if (stagingCount > 0 || !_options.EnableParquetWorkingFile)
            return stagingCount;

        var jobAudit = _jobAuditRepository.GetByJobId(jobId);
        return IsParquetAvailable(jobAudit) ? jobAudit!.WorkingFileRecordCount : 0;
    }

    public void DeleteByJobId(string jobId)
        => _inner.DeleteByJobId(jobId);

    private static bool IsParquetAvailable(IngestionJobAudit? jobAudit)
        => jobAudit != null
           && string.Equals(jobAudit.WorkingFileFormat, "Parquet", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(jobAudit.WorkingFilePath)
           && jobAudit.WorkingFileRecordCount > 0;
}
