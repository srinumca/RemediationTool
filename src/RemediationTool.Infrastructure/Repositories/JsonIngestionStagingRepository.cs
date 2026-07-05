using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.Strategies;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;
    private readonly IngestionProcessingOptions _processingOptions;
    private readonly ILogger<JsonIngestionStagingRepository> _logger;

    public JsonIngestionStagingRepository(
        IStorageService storage,
        IIngestionJobAuditRepository jobAuditRepository,
        IOptions<IngestionProcessingOptions> processingOptions,
        ILogger<JsonIngestionStagingRepository> logger,
        ILogger<ParquetIngestionWorkingFileStrategy> parquetLogger)
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "ingestion-staged-findings.json");
        _jobAuditRepository = jobAuditRepository;
        _processingOptions = processingOptions.Value;
        _logger = logger;
        _workingFileStrategy = new ParquetIngestionWorkingFileStrategy(storage, processingOptions, parquetLogger);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0)
            return;

        lock (_lock)
        {
            var stagedFindings = LoadAll();
            stagedFindings.RemoveAll(existing => string.Equals(existing.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            var newRecords = validFindings
                .Select((finding, index) => new IngestionStagedFinding
                {
                    JobId = jobId,
                    SequenceNumber = index + 1,
                    Finding = finding,
                    CreatedAtUtc = DateTime.UtcNow
                })
                .ToList();

            stagedFindings.AddRange(newRecords);
            SaveAll(stagedFindings);
        }

        WriteParquetWorkingFile(jobId, validFindings);
    }

    public List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<FileFinding>();

        var parquetRecords = TryReadFromParquet(jobId, lastProcessedRecordCount);
        if (parquetRecords != null)
            return parquetRecords;

        _logger.LogInformation(
            "[STAGING_RESUME_READ] JobId:{JobId}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            jobId, lastProcessedRecordCount);

        lock (_lock)
        {
            return LoadAll()
                .Where(record =>
                    string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase)
                    && record.SequenceNumber > lastProcessedRecordCount)
                .OrderBy(record => record.SequenceNumber)
                .Select(record => record.Finding)
                .ToList();
        }
    }

    public int CountByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return 0;

        lock (_lock)
        {
            var stagingCount = LoadAll()
                .Count(record => string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            if (stagingCount > 0 || !_processingOptions.EnableParquetWorkingFile)
                return stagingCount;
        }

        var audit = _jobAuditRepository.GetByJobId(jobId);
        return IsParquetAvailable(audit) ? audit!.WorkingFileRecordCount : 0;
    }

    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        lock (_lock)
        {
            var stagedFindings = LoadAll();
            var removed = stagedFindings.RemoveAll(record =>
                string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                SaveAll(stagedFindings);
        }
    }

    private void WriteParquetWorkingFile(string jobId, List<FileFinding> validFindings)
    {
        if (!_processingOptions.EnableParquetWorkingFile) return;

        var audit = _jobAuditRepository.GetByJobId(jobId);
        if (audit == null)
        {
            _logger.LogWarning("[PARQUET_WRITE_SKIPPED] JobId:{JobId}, Reason:Job audit not found.", jobId);
            return;
        }

        var result = _workingFileStrategy
            .WriteAsync(jobId, audit.InboundFileName, validFindings)
            .GetAwaiter()
            .GetResult();

        audit.WorkingFileFormat = result.Format;
        audit.WorkingFilePath = result.Path;
        audit.WorkingFileRecordCount = result.RecordCount;
        _jobAuditRepository.Update(audit);

        _logger.LogInformation(
            "[PARQUET_STAGING_WRITE_COMPLETE] JobId:{JobId}, Path:{Path}, Records:{Records}",
            jobId, result.Path, result.RecordCount);
    }

    private List<FileFinding>? TryReadFromParquet(string jobId, int lastProcessedRecordCount)
    {
        if (!_processingOptions.EnableParquetWorkingFile) return null;

        var audit = _jobAuditRepository.GetByJobId(jobId);
        if (!IsParquetAvailable(audit)) return null;

        try
        {
            _logger.LogInformation(
                "[PARQUET_RESUME_READ_ATTEMPT] JobId:{JobId}, Path:{Path}, LastProcessedRecordCount:{LastProcessedRecordCount}",
                jobId, audit!.WorkingFilePath, lastProcessedRecordCount);

            var records = _workingFileStrategy
                .ReadAfterAsync(audit.WorkingFilePath!, lastProcessedRecordCount)
                .GetAwaiter()
                .GetResult();

            if (records.Count > 0 || lastProcessedRecordCount >= audit.WorkingFileRecordCount)
            {
                _logger.LogInformation("[PARQUET_RESUME_READ_SUCCESS] JobId:{JobId}, Records:{Records}", jobId, records.Count);
                return records;
            }

            _logger.LogWarning("[PARQUET_RESUME_EMPTY_FALLBACK] JobId:{JobId}, Path:{Path}", jobId, audit.WorkingFilePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PARQUET_RESUME_READ_FAILED] JobId:{JobId}. Falling back to staging.", jobId);
            return null;
        }
    }

    private List<IngestionStagedFinding> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<IngestionStagedFinding>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionStagedFinding>();

        return JsonSerializer.Deserialize<List<IngestionStagedFinding>>(json, _jsonOptions)
               ?? new List<IngestionStagedFinding>();
    }

    private void SaveAll(List<IngestionStagedFinding> stagedFindings)
    {
        var json = JsonSerializer.Serialize(stagedFindings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static bool IsParquetAvailable(IngestionJobAudit? audit)
        => audit != null
           && string.Equals(audit.WorkingFileFormat, "Parquet", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(audit.WorkingFilePath)
           && audit.WorkingFileRecordCount > 0;
}
