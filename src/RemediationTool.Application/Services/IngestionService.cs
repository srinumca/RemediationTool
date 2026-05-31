using ClosedXML.Excel;
using CsvHelper;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using RemediationTool.Domain.Enums;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace RemediationTool.Application.Services;

public class IngestionService : IIngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly IValidator<FileFinding> _validator;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IRejectedRowRepository _rejectedRowRepository;
    private readonly IngestionProcessingOptions _processingOptions;
    private readonly IIngestionCheckpointRepository _checkpointRepository;
    private readonly IIngestionStagingRepository _stagingRepository;
    private readonly IIngestionWorkingFileStrategy _workingFileStrategy;

    private const long MaxUploadFileSizeBytes = 100 * 1024 * 1024; // 100 MB
    private static readonly string[] AllowedUploadExtensions = { ".csv", ".xlsx" };

    public IngestionService(
       ILogger<IngestionService> logger,
       IFileFindingRepository repository,
       IStorageService storage,
       IValidator<FileFinding> validator,
       IIngestionJobAuditRepository jobAuditRepository,
       IRejectedRowRepository rejectedRowRepository,
       IOptions<IngestionProcessingOptions> processingOptions,
       IIngestionCheckpointRepository checkpointRepository,
       IIngestionStagingRepository stagingRepository,
       IIngestionWorkingFileStrategy workingFileStrategy)
    {
        _logger = logger;
        _repository = repository;
        _storage = storage;
        _validator = validator;
        _jobAuditRepository = jobAuditRepository;
        _rejectedRowRepository = rejectedRowRepository;
        _processingOptions = processingOptions.Value;
        _checkpointRepository = checkpointRepository;
        _stagingRepository = stagingRepository;
        _workingFileStrategy = workingFileStrategy;
    }

    public async Task<IngestionUploadResponse> ProcessAsync(IFormFile file)
    {
        var startedAtUtc = DateTime.UtcNow;
        var jobId = IngestionJobIdGenerator.Generate();

        ValidateUploadedFile(file);

        ArgumentNullException.ThrowIfNull(file);

        var uploadedBy = "system";
        var loadTime = DateTime.UtcNow;
        var inboundFileName = file.FileName;
        var triggerType = "Manual";
        var ingestionMode = "Full";

        var configuredBatchSize = ResolveBatchSize();

        var response = new IngestionUploadResponse
        {
            JobId = jobId,
            InboundFileName = inboundFileName,
            StartedAtUtc = startedAtUtc,
            Status = IngestionJobStatus.Started,
            TriggerType = triggerType,
            IngestionMode = ingestionMode,
            BatchSize = configuredBatchSize,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount
        };

        var jobAudit = new IngestionJobAudit
        {
            JobId = jobId,
            InboundFileName = inboundFileName,
            UserName = uploadedBy,
            StartedBy = uploadedBy,
            StartTimestampUtc = startedAtUtc,
            Status = IngestionJobStatus.Started,
            TriggerType = triggerType,
            IngestionMode = ingestionMode,
            BatchSize = configuredBatchSize,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount
        };

        _jobAuditRepository.Add(jobAudit);

        var checkpoint = BuildCheckpoint(response, jobAudit, IngestionJobStatus.Started);
        _checkpointRepository.Upsert(checkpoint);

        try
        {
            _logger.LogInformation(
                "Ingestion started. JobId: {JobId}, FileName: {FileName}",
                jobId, inboundFileName);

            var archiveKey = IngestionArchivePathBuilder.BuildOriginalFilePath(
                jobId, inboundFileName, startedAtUtc);

            await _storage.UploadAsync(archiveKey, file.OpenReadStream());
            response.ArchivedFilePath = archiveKey;

            List<FileFinding> findings;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            using var stream = file.OpenReadStream();

            findings = ext switch
            {
                ".xlsx" => ParseExcel(stream, jobId, inboundFileName, uploadedBy, loadTime, response.RejectedRows),
                ".csv" => ParseCsv(stream, jobId, inboundFileName, uploadedBy, loadTime, response.RejectedRows),
                _ => throw new InvalidDataException("Unsupported file format. Only .csv and .xlsx files are allowed.")
            };

            response.TotalRecords = findings.Count;
            response.PayloadRecordCount = findings.Count;
            response.RejectCount = findings.Count(x => !x.IsValid);
            response.SuccessCount = findings.Count(x => x.IsValid);

            response.FindingTypeCounts = findings.Where(x => x.IsValid)
                .GroupBy(x => x.FindingType.ToString(),StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count());

            response.ValidationFailureCount = response.RejectCount;
            response.SourceSystem = ResolveSourceSystem(findings);

            PersistRejectedRows(jobId, inboundFileName, response.RejectedRows);

            var validFindings = findings.Where(x => x.IsValid).ToList();

            _stagingRepository.SaveValidFindings(jobId, validFindings);

            if (_processingOptions.EnableParquetWorkingFile && validFindings.Count > 0)
            {
                var workingFileResult = await _workingFileStrategy.WriteAsync(
                    jobId, inboundFileName, validFindings);

                response.WorkingFileFormat = workingFileResult.Format;
                response.WorkingFilePath = workingFileResult.Path;
                response.WorkingFileRecordCount = workingFileResult.RecordCount;
                jobAudit.WorkingFileFormat = workingFileResult.Format;
                jobAudit.WorkingFilePath = workingFileResult.Path;
                jobAudit.WorkingFileRecordCount = workingFileResult.RecordCount;

                _logger.LogInformation(
                    "Ingestion working file created. JobId: {JobId}, Format: {Format}, Path: {Path}, RecordCount: {RecordCount}",
                    jobId, workingFileResult.Format, workingFileResult.Path, workingFileResult.RecordCount);
            }

            PersistValidFindingsInBatches(validFindings, response, jobAudit, configuredBatchSize);

            response.Status = DetermineFinalStatus(response.SuccessCount, response.RejectCount);
            response.CompletedAtUtc = DateTime.UtcNow;

            UpdateCheckpoint(response, jobAudit, response.Status);

            response.Message = BuildResponseMessage(
                response.Status,
                response.SuccessCount,
                response.RejectCount);

            // Clean up staging data once the job is fully complete (Success or PartialSuccess).
            // Failed jobs retain their staged records so resume can pick up from the last checkpoint.
            if (response.Status == IngestionJobStatus.Success
                || response.Status == IngestionJobStatus.PartialSuccess)
            {
                try
                {
                    _stagingRepository.DeleteByJobId(jobId);
                    _logger.LogInformation(
                        "Staging records cleaned up after job completion. JobId: {JobId}", jobId);
                }
                catch (Exception cleanupEx)
                {
                    // Cleanup failure must never block the successful ingestion response.
                    // Log and continue — staging file will be cleaned up on next run for this jobId.
                    _logger.LogWarning(
                        cleanupEx,
                        "Failed to clean up staging records. JobId: {JobId}. Will be cleaned on next upload.", jobId);
                }
            }

            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);

            UpdateJobAudit(jobAudit, response);

            _logger.LogInformation(
                "Ingestion completed. JobId: {JobId}, Status: {Status}, Total: {Total}, Success: {Success}, Rejected: {Rejected}",
                jobId, response.Status, response.TotalRecords, response.SuccessCount, response.RejectCount);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed. JobId: {JobId}, FileName: {FileName}", jobId, inboundFileName);

            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Ingestion failed: {ex.Message}";
            response.ValidationFailureCount = response.RejectCount;
            response.SourceSystem ??= "Unknown";

            try { UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message); }
            catch (Exception checkpointEx)
            { _logger.LogError(checkpointEx, "Failed to update ingestion checkpoint. JobId: {JobId}", jobId); }

            try { UpdateJobAudit(jobAudit, response, ex.Message); }
            catch (Exception auditEx)
            { _logger.LogError(auditEx, "Failed to update ingestion job audit. JobId: {JobId}", jobId); }

            return response;
        }
    }

    private void PersistValidFindingsInBatches(
        List<FileFinding> validFindings,
        IngestionUploadResponse response,
        IngestionJobAudit jobAudit,
        int batchSize)
    {
        if (validFindings.Count == 0)
        {
            response.TotalBatches = 0;
            response.PersistedBatchCount = 0;
            response.LastSuccessfulBatchNumber = 0;
            response.LastProcessedRecordCount = 0;
            jobAudit.TotalBatches = 0;
            jobAudit.PersistedBatchCount = 0;
            jobAudit.LastSuccessfulBatchNumber = 0;
            jobAudit.LastProcessedRecordCount = 0;
            _jobAuditRepository.Update(jobAudit);
            return;
        }

        var batches = validFindings
            .Chunk(batchSize)
            .Select((items, index) => new { BatchNumber = index + 1, Records = items.ToList() })
            .ToList();

        response.TotalBatches = batches.Count;
        jobAudit.TotalBatches = batches.Count;
        _jobAuditRepository.Update(jobAudit);

        foreach (var batch in batches)
        {
            try
            {
                PersistBatchWithRetry(batch.Records, batch.BatchNumber, batches.Count, response, jobAudit);

                response.PersistedBatchCount++;
                response.LastSuccessfulBatchNumber = batch.BatchNumber;
                response.LastProcessedRecordCount += batch.Records.Count;
                jobAudit.PersistedBatchCount = response.PersistedBatchCount;
                jobAudit.LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber;
                jobAudit.LastProcessedRecordCount = response.LastProcessedRecordCount;
                jobAudit.BatchPersistenceRetryCount = response.BatchPersistenceRetryCount;

                if (_processingOptions.EnableBatchCheckpointing)
                {
                    UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
                    _jobAuditRepository.Update(jobAudit);
                }

                _logger.LogInformation(
                    "Ingestion batch persisted. JobId: {JobId}, BatchNumber: {BatchNumber}, TotalBatches: {TotalBatches}, BatchSize: {BatchSize}, RecordsPersisted: {RecordsPersisted}, RetryCount: {RetryCount}",
                    response.JobId, batch.BatchNumber, batches.Count, batchSize, batch.Records.Count, response.BatchPersistenceRetryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ingestion batch persistence failed after Polly retries. JobId: {JobId}, BatchNumber: {BatchNumber}, TotalBatches: {TotalBatches}, LastSuccessfulBatch: {LastSuccessfulBatch}, LastProcessedRecordCount: {LastProcessedRecordCount}",
                    response.JobId, batch.BatchNumber, batches.Count, response.LastSuccessfulBatchNumber, response.LastProcessedRecordCount);

                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message);
                _jobAuditRepository.Update(jobAudit);

                throw new InvalidOperationException(
                    $"Batch persistence failed at batch {batch.BatchNumber} of {batches.Count} after {_processingOptions.MaxBatchPersistenceRetryCount} retry attempt(s). Last successful batch: {response.LastSuccessfulBatchNumber}.",
                    ex);
            }
        }
    }

    private int ResolveBatchSize()
    {
        var size = _processingOptions.BatchSize;
        if (size < _processingOptions.MinBatchSize) return _processingOptions.MinBatchSize;
        if (size > _processingOptions.MaxBatchSize) return _processingOptions.MaxBatchSize;
        return size;
    }

    private static string ResolveSourceSystem(List<FileFinding> findings)
    {
        var systems = findings
            .Where(f => !string.IsNullOrWhiteSpace(f.OriginatingDataSystem))
            .Select(f => f.OriginatingDataSystem.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return systems.Count switch { 0 => "Unknown", 1 => systems[0], _ => "Multiple" };
    }

    private static string SerializeFindingAsRawRow(FileFinding finding)
    {
        return JsonSerializer.Serialize(new
        {
            finding.SourceRecordId,
            finding.FindingFileName,
            finding.FindingFileFormat,
            finding.FindingFileSizeBytes,
            finding.CurrentFileLocation,
            FindingType = finding.FindingType.ToString(),
            finding.DataSystem,
            finding.OriginatingDataSystem,
            finding.OriginatingVendorTool,
            finding.OriginalFileLocation,
            finding.QuarantineDateUtc,
            finding.LastModifiedDateUtc,
            finding.CreatedDateUtc,
            finding.LastAccessedDateUtc,
            finding.SiteOwner,
            finding.FileOwner,
            finding.BusinessUnit,
            finding.Division,
            finding.Department,
            finding.Region,
            finding.Country,
            finding.PolicyName,
            finding.PolicyId,
            finding.FindingReason,
            finding.RiskLevel,
            finding.SensitivityLabel,
            finding.DetectionDateUtc,
            finding.RecommendedAction,
            finding.RestorationTicketIdentifier,
            finding.RestorationRequestorEmail,
            finding.RestorationComment
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    private List<FileFinding> ParseExcel(
        Stream stream, string jobId, string inboundFileName,
        string uploadedBy, DateTime loadTime, List<RejectedRowSummary> rejectedRows)
    {
        var findings = new List<FileFinding>();
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var headerMap = BuildExcelHeaderMap(sheet);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNumber = row.RowNumber();
            var finding = MapExcelRowToFinding(row, headerMap, jobId, inboundFileName, uploadedBy, loadTime);

            if (IsBlankFinding(finding))
            {
                _logger.LogDebug("Blank Excel row skipped. JobId: {JobId}, RowNumber: {RowNumber}", jobId, rowNumber);
                continue;
            }

            ApplyValidation(finding, rowNumber, rejectedRows);
            findings.Add(finding);
        }

        return findings;
    }

    private static bool IsBlankFinding(FileFinding finding)
    {
        return string.IsNullOrWhiteSpace(finding.SourceRecordId)
            && string.IsNullOrWhiteSpace(finding.FindingFileName)
            && string.IsNullOrWhiteSpace(finding.FindingFileFormat)
            && !finding.FindingFileSizeBytes.HasValue
            && string.IsNullOrWhiteSpace(finding.CurrentFileLocation)
            && finding.FindingType == default
            && string.IsNullOrWhiteSpace(finding.OriginatingDataSystem)
            && string.IsNullOrWhiteSpace(finding.OriginatingVendorTool)
            && string.IsNullOrWhiteSpace(finding.OriginalFileLocation)
            && !finding.QuarantineDateUtc.HasValue
            && string.IsNullOrWhiteSpace(finding.SiteOwner)
            && string.IsNullOrWhiteSpace(finding.FileOwner);
    }

    private List<FileFinding> ParseCsv(
        Stream stream, string jobId, string inboundFileName,
        string uploadedBy, DateTime loadTime, List<RejectedRowSummary> rejectedRows)
    {
        var findings = new List<FileFinding>();
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        try
        {
            if (!csv.Read())
                throw new InvalidDataException("CSV file does not contain a header row.");
            csv.ReadHeader();
            ValidateHeaders(csv.HeaderRecord?.ToList() ?? new List<string>());
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"CSV header validation failed. {ex.Message}", ex);
        }

        while (true)
        {
            var rowNumber = csv.Context.Parser.Row;

            try
            {
                if (!csv.Read()) break;
                rowNumber = csv.Context.Parser.Row;

                var finding = MapCsvRowToFinding(csv, jobId, inboundFileName, uploadedBy, loadTime);

                if (IsBlankFinding(finding))
                {
                    _logger.LogDebug("Blank CSV row skipped. JobId: {JobId}, RowNumber: {RowNumber}", jobId, rowNumber);
                    continue;
                }

                ApplyValidation(finding, rowNumber, rejectedRows);
                findings.Add(finding);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Malformed CSV row skipped. JobId: {JobId}, RowNumber: {RowNumber}", jobId, rowNumber);

                // Only create a RejectedRowSummary — do NOT add a FileFinding for malformed rows
                rejectedRows.Add(new RejectedRowSummary
                {
                    RejectedRowId = Guid.NewGuid().ToString("N"),
                    SourceRecordId = null,
                    FindingFileName = null,
                    FindingType = null,
                    UserName = uploadedBy,
                    RowNumber = rowNumber,
                    FieldName = "CSV_ROW",
                    RejectedValue = null,
                    ErrorReason = $"Malformed CSV row. {ex.Message}",
                    ErrorDateUtc = DateTime.UtcNow,
                    RawRowJson = null
                });
            }
        }

        return findings;
    }

    private FileFinding MapExcelRowToFinding(
        IXLRow row, Dictionary<string, int> headerMap,
        string jobId, string inboundFileName, string uploadedBy, DateTime loadTime)
    {
        return new FileFinding
        {
            // Id is always system-generated — never derived from inbound data
            Id = Guid.NewGuid(),
            RecordVersionId = Guid.NewGuid().ToString("N"),
            SourceRecordId = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.SourceRecordId)),

            FindingFileName = GetExcelValue(row, headerMap, InboundLayoutColumns.FindingFileName),
            FindingFileFormat = GetExcelValue(row, headerMap, InboundLayoutColumns.FindingFileFormat),
            FindingFileSizeBytes = TryParseNullableLong(GetExcelValue(row, headerMap, InboundLayoutColumns.FindingFileSize)),
            CurrentFileLocation = GetExcelValue(row, headerMap, InboundLayoutColumns.CurrentFileLocation),
            FindingType = ParseFindingType(GetExcelValue(row, headerMap, InboundLayoutColumns.FindingType)),
            DataSystem = GetExcelValue(row, headerMap, InboundLayoutColumns.DataSystem),

            OriginatingDataSystem = GetExcelValue(row, headerMap, InboundLayoutColumns.OriginatingDataSystem),
            OriginatingVendorTool = GetExcelValue(row, headerMap, InboundLayoutColumns.OriginatingVendorTool),

            LastModifiedDateUtc = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.LastModifiedDate)),
            CreatedDateUtc = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.CreatedDate)),
            LastAccessedDateUtc = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.LastAccessedDate)),

            SiteOwner = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.SiteOwner)),
            FileOwner = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.FileOwner)),
            BusinessUnit = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.BusinessUnit)),
            Division = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.Division)),
            Department = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.Department)),
            Region = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.Region)),
            Country = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.Country)),

            PolicyName = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.PolicyName)),
            PolicyId = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.PolicyId)),
            FindingReason = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.FindingReason)),
            RiskLevel = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.RiskLevel)),
            SensitivityLabel = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.SensitivityLabel)),
            DetectionDateUtc = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.DetectionDate)),
            RecommendedAction = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.RecommendedAction)),

            OriginalFileLocation = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.OriginalFileLocation)),
            QuarantineDateUtc = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.QuarantineDate)),

            RestorationTicketIdentifier = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.RestorationTicketIdentifier)),
            RestorationRequestorEmail = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.RestorationRequestorEmail)),
            RestorationComment = NullIfWhiteSpace(GetExcelValue(row, headerMap, InboundLayoutColumns.RestorationComment)),

            IngestionJobId = jobId,
            InboundFileName = inboundFileName,
            UserName = uploadedBy,
            LoadDateUtc = loadTime,
            LastUpdateDateUtc = loadTime
        };
    }

    private FileFinding MapCsvRowToFinding(
        CsvReader csv, string jobId, string inboundFileName,
        string uploadedBy, DateTime loadTime)
    {
        return new FileFinding
        {
            // Id is always system-generated — never derived from inbound data
            Id = Guid.NewGuid(),
            RecordVersionId = Guid.NewGuid().ToString("N"),
            SourceRecordId = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.SourceRecordId)),

            FindingFileName = GetCsvValue(csv, InboundLayoutColumns.FindingFileName),
            FindingFileFormat = GetCsvValue(csv, InboundLayoutColumns.FindingFileFormat),
            FindingFileSizeBytes = TryParseNullableLong(GetCsvValue(csv, InboundLayoutColumns.FindingFileSize)),
            CurrentFileLocation = GetCsvValue(csv, InboundLayoutColumns.CurrentFileLocation),
            FindingType = ParseFindingType(GetCsvValue(csv, InboundLayoutColumns.FindingType)),
            DataSystem = GetCsvValue(csv, InboundLayoutColumns.DataSystem),

            OriginatingDataSystem = GetCsvValue(csv, InboundLayoutColumns.OriginatingDataSystem),
            OriginatingVendorTool = GetCsvValue(csv, InboundLayoutColumns.OriginatingVendorTool),

            LastModifiedDateUtc = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.LastModifiedDate)),
            CreatedDateUtc = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.CreatedDate)),
            LastAccessedDateUtc = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.LastAccessedDate)),

            SiteOwner = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.SiteOwner)),
            FileOwner = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.FileOwner)),
            BusinessUnit = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.BusinessUnit)),
            Division = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.Division)),
            Department = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.Department)),
            Region = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.Region)),
            Country = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.Country)),

            PolicyName = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.PolicyName)),
            PolicyId = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.PolicyId)),
            FindingReason = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.FindingReason)),
            RiskLevel = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.RiskLevel)),
            SensitivityLabel = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.SensitivityLabel)),
            DetectionDateUtc = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.DetectionDate)),
            RecommendedAction = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.RecommendedAction)),

            OriginalFileLocation = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.OriginalFileLocation)),
            QuarantineDateUtc = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.QuarantineDate)),

            RestorationTicketIdentifier = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.RestorationTicketIdentifier)),
            RestorationRequestorEmail = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.RestorationRequestorEmail)),
            RestorationComment = NullIfWhiteSpace(GetCsvValue(csv, InboundLayoutColumns.RestorationComment)),

            IngestionJobId = jobId,
            InboundFileName = inboundFileName,
            UserName = uploadedBy,
            LoadDateUtc = loadTime,
            LastUpdateDateUtc = loadTime
        };
    }

    /// <summary>
    /// Parses the inbound string value for Finding_Type into the FindingType enum.
    /// Normalises "Not Obsolete" → "NotObsolete" before enum parsing.
    /// Invalid values fall through to default (FindingType.Obsolete = 0) and will be
    /// caught by FileFindingValidator.IsInEnum() as an invalid enum value.
    /// </summary>
    private static FindingType ParseFindingType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return default;

        // Normalise spaced variants: "Not Obsolete" → "NotObsolete"
        var normalised = value.Trim().Replace(" ", string.Empty);

        return Enum.TryParse<FindingType>(normalised, ignoreCase: true, out var result)
            ? result
            : default;  // Invalid value — caught by IsInEnum() in FileFindingValidator
    }

    private void ApplyValidation(
        FileFinding finding, int rowNumber, List<RejectedRowSummary> rejectedRows)
    {
        var result = _validator.Validate(finding);

        if (result.IsValid)
        {
            finding.IsValid = true;
            finding.IngestionErrorReason = string.Empty;
            return;
        }

        finding.IsValid = false;
        finding.IngestionErrorReason = string.Join(", ", result.Errors.Select(e => e.ErrorMessage));

        var rawRowJson = SerializeFindingAsRawRow(finding);

        foreach (var error in result.Errors)
        {
            rejectedRows.Add(new RejectedRowSummary
            {
                RejectedRowId = Guid.NewGuid().ToString("N"),
                SourceRecordId = finding.SourceRecordId,
                FindingFileName = finding.FindingFileName,
                FindingType = finding.FindingType.ToString(),
                UserName = finding.UserName,
                RowNumber = rowNumber,
                FieldName = error.PropertyName,
                RejectedValue = GetRejectedValue(finding, error.PropertyName),
                ErrorReason = error.ErrorMessage,
                ErrorDateUtc = DateTime.UtcNow,
                RawRowJson = rawRowJson
            });
        }
    }

    private IngestionJobStatus DetermineFinalStatus(int successCount, int rejectCount)
    {
        if (successCount > 0 && rejectCount == 0) return IngestionJobStatus.Success;
        if (successCount > 0 && rejectCount > 0) return IngestionJobStatus.PartialSuccess;
        return IngestionJobStatus.Failed;
    }

    private string BuildResponseMessage(IngestionJobStatus status, int successCount, int rejectCount)
    {
        return status switch
        {
            IngestionJobStatus.Success => "File processed successfully.",
            IngestionJobStatus.PartialSuccess => $"File processed with partial success. Success: {successCount}, Rejected: {rejectCount}.",
            IngestionJobStatus.Failed => "File processing failed. No valid records were ingested.",
            _ => "File processing completed."
        };
    }

    private string? GetRejectedValue(FileFinding finding, string propertyName)
    {
        return propertyName switch
        {
            nameof(FileFinding.SourceRecordId) => finding.SourceRecordId,
            nameof(FileFinding.FindingFileName) => finding.FindingFileName,
            nameof(FileFinding.FindingFileFormat) => finding.FindingFileFormat,
            nameof(FileFinding.FindingFileSizeBytes) => finding.FindingFileSizeBytes?.ToString(CultureInfo.InvariantCulture),
            nameof(FileFinding.CurrentFileLocation) => finding.CurrentFileLocation,
            nameof(FileFinding.FindingType) => finding.FindingType.ToString(),
            nameof(FileFinding.DataSystem) => finding.DataSystem,
            nameof(FileFinding.OriginatingDataSystem) => finding.OriginatingDataSystem,
            nameof(FileFinding.OriginatingVendorTool) => finding.OriginatingVendorTool,
            nameof(FileFinding.LastModifiedDateUtc) => finding.LastModifiedDateUtc?.ToString("O"),
            nameof(FileFinding.CreatedDateUtc) => finding.CreatedDateUtc?.ToString("O"),
            nameof(FileFinding.LastAccessedDateUtc) => finding.LastAccessedDateUtc?.ToString("O"),
            nameof(FileFinding.OriginalFileLocation) => finding.OriginalFileLocation,
            nameof(FileFinding.QuarantineDateUtc) => finding.QuarantineDateUtc?.ToString("O"),
            nameof(FileFinding.ExceptionDateUtc) => finding.ExceptionDateUtc?.ToString("O"),
            nameof(FileFinding.RestorationTicketIdentifier) => finding.RestorationTicketIdentifier,
            nameof(FileFinding.RestorationRequestorEmail) => finding.RestorationRequestorEmail,
            nameof(FileFinding.RestorationComment) => finding.RestorationComment,
            _ => null
        };
    }

    private void ValidateHeaders(IEnumerable<string?> headers)
    {
        var headerList = headers
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.Trim())
            .ToList();

        if (headerList.Count == 0)
            throw new InvalidDataException("Uploaded file does not contain any headers.");

        var duplicateHeaders = headerList
            .GroupBy(NormalizeHeader, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .ToList();

        if (duplicateHeaders.Any())
            throw new InvalidDataException(
                $"Uploaded file contains duplicate headers: {string.Join(", ", duplicateHeaders)}");

        var normalizedHeaders = headerList.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingRequired = InboundLayoutColumns.RequiredColumns
            .Where(col => !normalizedHeaders.Contains(NormalizeHeader(col)))
            .ToList();

        if (missingRequired.Any())
            throw new InvalidDataException(
                $"Uploaded file is missing required columns: {string.Join(", ", missingRequired)}");
    }

    private Dictionary<string, int> BuildExcelHeaderMap(IXLWorksheet sheet)
    {
        var headerRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("Excel file does not contain a header row.");

        var headers = headerRow.CellsUsed().Select(c => c.GetString()).ToList();
        ValidateHeaders(headers);

        return headerRow.CellsUsed()
            .GroupBy(c => NormalizeHeader(c.GetString()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
    }

    private string GetExcelValue(IXLRow row, Dictionary<string, int> headerMap, string columnName)
    {
        var key = NormalizeHeader(columnName);
        return headerMap.TryGetValue(key, out var col)
            ? row.Cell(col).GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private string GetCsvValue(CsvReader csv, string columnName)
    {
        try
        {
            var headers = csv.HeaderRecord;
            if (headers == null || headers.Length == 0) return string.Empty;

            var match = headers.FirstOrDefault(h =>
                string.Equals(NormalizeHeader(h), NormalizeHeader(columnName), StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(match)
                ? string.Empty
                : csv.GetField(match)?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private DateTime? TryParseNullableDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private long? TryParseNullableLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = value.Replace(",", string.Empty).Trim();
        if (long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return Convert.ToInt64(d);
        return null;
    }

    private string? NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
    }

    private async Task<string> StoreProcessingSummaryAsync(IngestionUploadResponse response)
    {
        var summary = new ProcessingSummaryArtifact
        {
            JobId = response.JobId,
            InboundFileName = response.InboundFileName,
            SourceSystem = response.SourceSystem,
            TriggerType = response.TriggerType,
            IngestionMode = response.IngestionMode,
            StartedAtUtc = response.StartedAtUtc,
            CompletedAtUtc = response.CompletedAtUtc,
            ProcessingStartTimeUtc = response.StartedAtUtc,
            ProcessingEndTimeUtc = response.CompletedAtUtc,
            PayloadRecordCount = response.PayloadRecordCount,
            TotalRowsProcessed = response.TotalRecords,
            SuccessfulRows = response.SuccessCount,
            FailedRows = response.RejectCount,
            ValidationFailureCount = response.ValidationFailureCount,
            BatchSize = response.BatchSize,
            TotalBatches = response.TotalBatches,
            PersistedBatchCount = response.PersistedBatchCount,
            LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = response.LastProcessedRecordCount,
            CheckpointingEnabled = response.CheckpointingEnabled,
            BatchPersistenceRetryCount = response.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount = response.MaxBatchPersistenceRetryCount,
            WorkingFileFormat = response.WorkingFileFormat,
            WorkingFilePath = response.WorkingFilePath,
            WorkingFileRecordCount = response.WorkingFileRecordCount,
            IsResumeEligible = response.IsResumeEligible,
            LastCheckpointUtc = response.LastCheckpointUtc,
            CheckpointMessage = response.CheckpointMessage,
            FinalJobStatus = response.Status,
            Message = response.Message,
            ArchivedFilePath = response.ArchivedFilePath,
            RejectedRows = response.RejectedRows
        };

        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });

        var summaryKey = IngestionArchivePathBuilder.BuildProcessingSummaryPath(response.JobId, response.StartedAtUtc);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _storage.UploadAsync(summaryKey, stream);
        return summaryKey;
    }

    private void UpdateJobAudit(
     IngestionJobAudit audit,
     IngestionUploadResponse response,
     string? errorMessage = null)
    {
        audit.EndTimestampUtc = response.CompletedAtUtc;
        audit.SourceSystem = response.SourceSystem;
        audit.TriggerType = response.TriggerType;
        audit.IngestionMode = response.IngestionMode;
        audit.PayloadRecordCount = response.PayloadRecordCount;
        audit.TotalRecords = response.TotalRecords;
        audit.SuccessCount = response.SuccessCount;
        audit.RejectCount = response.RejectCount;
        audit.ValidationFailureCount = response.ValidationFailureCount;

        // FindingType breakdown — counts of valid records per type in this job.
        // Populates FindingTypeCounts only when we have access to the valid findings
        // via the response's RejectedRows complement. We derive from the response
        // SuccessCount total; however the per-type breakdown must come from the
        // FindingTypeCounts already set on the response if available, otherwise
        // we leave it as-is (it was already populated before this call).
        if (response.FindingTypeCounts != null && response.FindingTypeCounts.Count > 0)
            audit.FindingTypeCounts = response.FindingTypeCounts;
    }

    private void PersistRejectedRows(string jobId, string inboundFileName, List<RejectedRowSummary> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0) return;

        var details = rejectedRows.Select(row => new RejectedRowDetail
        {
            RejectedRowId = string.IsNullOrWhiteSpace(row.RejectedRowId) ? Guid.NewGuid().ToString("N") : row.RejectedRowId,
            JobId = jobId,
            InboundFileName = inboundFileName,
            SourceRecordId = row.SourceRecordId,
            FindingFileName = row.FindingFileName,
            FindingType = row.FindingType,
            UserName = row.UserName,
            RowNumber = row.RowNumber,
            FieldName = row.FieldName,
            RejectedValue = row.RejectedValue,
            ErrorReason = row.ErrorReason,
            ErrorDateUtc = row.ErrorDateUtc == default ? DateTime.UtcNow : row.ErrorDateUtc,
            RawRowJson = row.RawRowJson
        }).ToList();

        _rejectedRowRepository.AddRange(details);
    }

    private void ValidateUploadedFile(IFormFile? file)
    {
        var extension = file == null ? string.Empty : Path.GetExtension(file.FileName);

        var errors = new (Func<bool> IsInvalid, string ErrorMessage)[]
        {
            (() => file == null,                                                                                        "Uploaded file is required."),
            (() => file?.Length == 0,                                                                                  "Uploaded file is empty."),
            (() => file?.Length > MaxUploadFileSizeBytes,                                                              $"Uploaded file size exceeds the allowed limit of {MaxUploadFileSizeBytes / (1024 * 1024)} MB."),
            (() => file != null && string.IsNullOrWhiteSpace(extension),                                               "Uploaded file must have a valid file extension."),
            (() => file != null && !string.IsNullOrWhiteSpace(extension) && !AllowedUploadExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase), "Unsupported file format. Only .csv and .xlsx files are allowed.")
        }
        .Where(r => r.IsInvalid())
        .Select(r => r.ErrorMessage)
        .ToList();

        if (errors.Any())
            throw new InvalidDataException(string.Join(" ", errors));
    }

    private void PersistBatchWithRetry(
        List<FileFinding> records, int batchNumber, int totalBatches,
        IngestionUploadResponse response, IngestionJobAudit jobAudit)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(0, _processingOptions.MaxBatchPersistenceRetryCount),
                Delay = TimeSpan.FromMilliseconds(Math.Max(0, _processingOptions.BatchPersistenceRetryDelayMilliseconds)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<IOException>()
                    .Handle<TimeoutException>()
                    .Handle<InvalidOperationException>(),
                OnRetry = args =>
                {
                    response.BatchPersistenceRetryCount++;
                    jobAudit.BatchPersistenceRetryCount = response.BatchPersistenceRetryCount;
                    if (_processingOptions.EnableBatchCheckpointing)
                        _jobAuditRepository.Update(jobAudit);
                    _logger.LogWarning(args.Outcome.Exception,
                        "Polly retry triggered. JobId: {JobId}, BatchNumber: {BatchNumber}, TotalBatches: {TotalBatches}, Attempt: {AttemptNumber}, Delay: {RetryDelay}",
                        response.JobId, batchNumber, totalBatches, args.AttemptNumber + 1, args.RetryDelay);
                    return default;
                }
            })
            .Build();

        pipeline.Execute(() => _repository.AddRange(records));
    }

    private static IngestionCheckpoint BuildCheckpoint(
        IngestionUploadResponse response, IngestionJobAudit jobAudit,
        IngestionJobStatus status, string? failureReason = null)
    {
        return new IngestionCheckpoint
        {
            JobId = response.JobId,
            InboundFileName = response.InboundFileName ?? string.Empty,
            UserName = jobAudit.UserName,
            SourceSystem = response.SourceSystem,
            TriggerType = response.TriggerType,
            IngestionMode = response.IngestionMode,
            BatchSize = response.BatchSize,
            TotalBatches = response.TotalBatches,
            LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = response.LastProcessedRecordCount,
            PersistedBatchCount = response.PersistedBatchCount,
            SuccessCount = response.SuccessCount,
            RejectCount = response.RejectCount,
            BatchPersistenceRetryCount = response.BatchPersistenceRetryCount,
            Status = status,
            IsResumeEligible = status == IngestionJobStatus.Failed
            && response.TotalBatches > 0
            && response.LastSuccessfulBatchNumber < response.TotalBatches,
            LastCheckpointUtc = DateTime.UtcNow,
            FailureReason = failureReason,

            // Working file — persisted so resume can load from Parquet instead of JSON staging
            WorkingFilePath = response.WorkingFilePath,
            WorkingFileFormat = response.WorkingFileFormat,
            WorkingFileRecordCount = response.WorkingFileRecordCount
        };
    }

    private void UpdateCheckpoint(
        IngestionUploadResponse response, IngestionJobAudit jobAudit,
        IngestionJobStatus status, string? failureReason = null)
    {
        if (!_processingOptions.EnableBatchCheckpointing) return;

        var checkpoint = BuildCheckpoint(response, jobAudit, status, failureReason);
        _checkpointRepository.Upsert(checkpoint);

        response.IsResumeEligible = checkpoint.IsResumeEligible;
        response.LastCheckpointUtc = checkpoint.LastCheckpointUtc;
        response.CheckpointMessage = checkpoint.IsResumeEligible
            ? $"Ingestion can resume from batch {checkpoint.LastSuccessfulBatchNumber + 1}."
            : "Checkpoint updated.";

        jobAudit.IsResumeEligible = response.IsResumeEligible;
        jobAudit.LastCheckpointUtc = response.LastCheckpointUtc;
        jobAudit.CheckpointMessage = response.CheckpointMessage;
    }

    private IngestionUploadResponse BuildResumeResponseFromCheckpoint(IngestionCheckpoint checkpoint)
    {
        return new IngestionUploadResponse
        {
            JobId = checkpoint.JobId,
            InboundFileName = checkpoint.InboundFileName,
            SourceSystem = checkpoint.SourceSystem,
            TriggerType = "Resume",
            IngestionMode = checkpoint.IngestionMode,
            StartedAtUtc = DateTime.UtcNow,
            Status = IngestionJobStatus.Started,
            BatchSize = checkpoint.BatchSize > 0 ? checkpoint.BatchSize : ResolveBatchSize(),
            TotalBatches = checkpoint.TotalBatches,
            PersistedBatchCount = checkpoint.PersistedBatchCount,
            LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = checkpoint.LastProcessedRecordCount,
            SuccessCount = checkpoint.SuccessCount,
            RejectCount = checkpoint.RejectCount,
            TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount,
            PayloadRecordCount = checkpoint.SuccessCount + checkpoint.RejectCount,
            BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            IsResumeEligible = checkpoint.IsResumeEligible,
            LastCheckpointUtc = checkpoint.LastCheckpointUtc,
            CheckpointMessage = checkpoint.IsResumeEligible
                ? $"Resume started from batch {checkpoint.LastSuccessfulBatchNumber + 1}."
                : "Job is not eligible for resume."
        };
    }

    private void PersistRemainingFindingsInBatches(
        List<FileFinding> remainingFindings, IngestionUploadResponse response,
        IngestionJobAudit jobAudit, int batchSize, int lastSuccessfulBatchNumber)
    {
        if (remainingFindings.Count == 0) return;

        var batches = remainingFindings
            .Chunk(batchSize)
            .Select((items, index) => new { BatchNumber = lastSuccessfulBatchNumber + index + 1, Records = items.ToList() })
            .ToList();

        foreach (var batch in batches)
        {
            try
            {
                PersistBatchWithRetry(batch.Records, batch.BatchNumber, response.TotalBatches, response, jobAudit);

                response.PersistedBatchCount++;
                response.LastSuccessfulBatchNumber = batch.BatchNumber;
                response.LastProcessedRecordCount += batch.Records.Count;
                jobAudit.PersistedBatchCount = response.PersistedBatchCount;
                jobAudit.LastSuccessfulBatchNumber = response.LastSuccessfulBatchNumber;
                jobAudit.LastProcessedRecordCount = response.LastProcessedRecordCount;
                jobAudit.BatchPersistenceRetryCount = response.BatchPersistenceRetryCount;

                if (_processingOptions.EnableBatchCheckpointing)
                {
                    UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Started);
                    _jobAuditRepository.Update(jobAudit);
                }

                _logger.LogInformation(
                    "Resume ingestion batch persisted. JobId: {JobId}, BatchNumber: {BatchNumber}, TotalBatches: {TotalBatches}, RecordsPersisted: {RecordsPersisted}",
                    response.JobId, batch.BatchNumber, response.TotalBatches, batch.Records.Count);
            }
            catch (Exception ex)
            {
                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message);
                _jobAuditRepository.Update(jobAudit);
                throw new InvalidOperationException(
                    $"Resume batch persistence failed at batch {batch.BatchNumber} of {response.TotalBatches}. Last successful batch: {response.LastSuccessfulBatchNumber}.", ex);
            }
        }
    }

    public async Task<IngestionUploadResponse> ResumeAsync(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new IngestionUploadResponse { JobId = jobId, Status = IngestionJobStatus.Failed, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow, Message = "JobId is required." };

        var checkpoint = _checkpointRepository.GetByJobId(jobId);

        if (checkpoint == null)
            return new IngestionUploadResponse { JobId = jobId, Status = IngestionJobStatus.Failed, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow, Message = "No checkpoint found for the provided JobId." };

        if (!checkpoint.IsResumeEligible)
            return new IngestionUploadResponse { JobId = jobId, InboundFileName = checkpoint.InboundFileName, Status = checkpoint.Status, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow, Message = "This ingestion job is not eligible for resume.", IsResumeEligible = false, LastCheckpointUtc = checkpoint.LastCheckpointUtc, CheckpointMessage = checkpoint.FailureReason };

        var response = BuildResumeResponseFromCheckpoint(checkpoint);

        var jobAudit = new IngestionJobAudit
        {
            JobId = checkpoint.JobId,
            InboundFileName = checkpoint.InboundFileName,
            UserName = checkpoint.UserName,
            StartedBy = checkpoint.UserName,
            StartTimestampUtc = response.StartedAtUtc,
            SourceSystem = checkpoint.SourceSystem,
            TriggerType = "Resume",
            IngestionMode = checkpoint.IngestionMode,
            BatchSize = response.BatchSize,
            TotalBatches = checkpoint.TotalBatches,
            PersistedBatchCount = checkpoint.PersistedBatchCount,
            LastSuccessfulBatchNumber = checkpoint.LastSuccessfulBatchNumber,
            LastProcessedRecordCount = checkpoint.LastProcessedRecordCount,
            SuccessCount = checkpoint.SuccessCount,
            RejectCount = checkpoint.RejectCount,
            TotalRecords = checkpoint.SuccessCount + checkpoint.RejectCount,
            PayloadRecordCount = checkpoint.SuccessCount + checkpoint.RejectCount,
            BatchPersistenceRetryCount = checkpoint.BatchPersistenceRetryCount,
            MaxBatchPersistenceRetryCount = _processingOptions.MaxBatchPersistenceRetryCount,
            CheckpointingEnabled = _processingOptions.EnableBatchCheckpointing,
            Status = IngestionJobStatus.Started,
            IsResumeEligible = checkpoint.IsResumeEligible,
            LastCheckpointUtc = checkpoint.LastCheckpointUtc,
            CheckpointMessage = response.CheckpointMessage
        };

        try
        {
            _logger.LogInformation("Resume ingestion started. JobId: {JobId}, LastSuccessfulBatch: {LastSuccessfulBatch}, TotalBatches: {TotalBatches}",
                jobId, checkpoint.LastSuccessfulBatchNumber, checkpoint.TotalBatches);

            List<FileFinding> recordsToResume;
            try
            {
                recordsToResume = await LoadRecordsForResumeAsync(jobId, checkpoint, response);
            }
            catch (Exception ex)
            {
                response.Status = IngestionJobStatus.Failed;
                response.CompletedAtUtc = DateTime.UtcNow;
                response.Message = ex.Message;
                response.IsResumeEligible = false;
                response.CheckpointMessage = "No staged records found for resume.";
                UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, response.Message);
                UpdateJobAudit(jobAudit, response, response.Message);
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);
                return response;
            }

            if (recordsToResume.Count == 0)
            {
                response.Status = IngestionJobStatus.Success;
                response.CompletedAtUtc = DateTime.UtcNow;
                response.IsResumeEligible = false;
                response.Message = "No remaining records found to resume. Job appears to be already completed.";
                response.CheckpointMessage = "Resume completed. No pending records.";
                UpdateCheckpoint(response, jobAudit, response.Status);
                UpdateJobAudit(jobAudit, response);
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);
                return response;
            }

            PersistRemainingFindingsInBatches(recordsToResume, response, jobAudit, response.BatchSize, checkpoint.LastSuccessfulBatchNumber);

            response.Status = IngestionJobStatus.Success;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.IsResumeEligible = false;
            response.Message = "Ingestion resume completed successfully.";
            response.CheckpointMessage = "Resume completed successfully.";

            // Clean up staging records — resume is complete, no longer needed.
            try
            {
                _stagingRepository.DeleteByJobId(jobId);
                _logger.LogInformation(
                    "Staging records cleaned up after resume completion. JobId: {JobId}", jobId);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(
                    cleanupEx,
                    "Failed to clean up staging records after resume. JobId: {JobId}.", jobId);
            }

            UpdateCheckpoint(response, jobAudit, response.Status);
            UpdateJobAudit(jobAudit, response);

            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);

            _logger.LogInformation("Resume ingestion completed. JobId: {JobId}, Status: {Status}, LastSuccessfulBatch: {LastSuccessfulBatch}",
                jobId, response.Status, response.LastSuccessfulBatchNumber);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume ingestion failed. JobId: {JobId}", jobId);
            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Resume ingestion failed: {ex.Message}";
            response.IsResumeEligible = response.LastSuccessfulBatchNumber < response.TotalBatches;
            response.CheckpointMessage = response.IsResumeEligible
                ? $"Resume can continue from batch {response.LastSuccessfulBatchNumber + 1}."
                : "Resume failed.";
            UpdateCheckpoint(response, jobAudit, IngestionJobStatus.Failed, ex.Message);
            UpdateJobAudit(jobAudit, response, ex.Message);
            try { response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response); }
            catch (Exception summaryEx) { _logger.LogError(summaryEx, "Failed to store resume processing summary. JobId: {JobId}", jobId); }
            return response;
        }
    }

    private async Task<List<FileFinding>> LoadRecordsForResumeAsync(
    string jobId,
    IngestionCheckpoint checkpoint,
    IngestionUploadResponse response)
    {
        // --- Parquet path (preferred) ---
        // Use Parquet working file when available — faster reads, smaller footprint,
        // works correctly for both local storage and S3.
        if (!string.IsNullOrWhiteSpace(checkpoint.WorkingFilePath))
        {
            _logger.LogInformation(
                "Loading resume records from Parquet working file. JobId: {JobId}, Path: {Path}, LastProcessedRecordCount: {LastProcessedRecordCount}",
                jobId,
                checkpoint.WorkingFilePath,
                checkpoint.LastProcessedRecordCount);

            try
            {
                var parquetRecords = await _workingFileStrategy.ReadAfterAsync(
                    checkpoint.WorkingFilePath,
                    checkpoint.LastProcessedRecordCount);

                if (parquetRecords.Count > 0)
                {
                    _logger.LogInformation(
                        "Loaded {Count} records from Parquet for resume. JobId: {JobId}",
                        parquetRecords.Count, jobId);

                    return parquetRecords;
                }

                // Parquet returned zero records — fall through to JSON staging as safety net
                _logger.LogWarning(
                    "Parquet working file returned 0 records. Falling back to JSON staging. JobId: {JobId}",
                    jobId);
            }
            catch (Exception ex)
            {
                // Parquet read failure must not block resume — fall back to JSON staging
                _logger.LogWarning(
                    ex,
                    "Parquet read failed. Falling back to JSON staging. JobId: {JobId}, Path: {Path}",
                    jobId,
                    checkpoint.WorkingFilePath);
            }
        }

        // --- JSON staging fallback ---
        // Used when: no Parquet working file exists, Parquet read fails, or Parquet returns 0 records.
        _logger.LogInformation(
            "Loading resume records from JSON staging. JobId: {JobId}, LastProcessedRecordCount: {LastProcessedRecordCount}",
            jobId,
            checkpoint.LastProcessedRecordCount);

        var stagedRecordCount = _stagingRepository.CountByJobId(jobId);

        if (stagedRecordCount == 0)
        {
            throw new InvalidOperationException(
                "Resume failed. No staged records were found for this JobId, and no Parquet working file is available. Re-upload may be required.");
        }

        return _stagingRepository.GetValidFindingsAfter(jobId, checkpoint.LastProcessedRecordCount);
    }

}