using ClosedXML.Excel;
using CsvHelper;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemediationTool.Application.Services;

public class IngestionService
{
    private readonly ILogger<IngestionService> _logger;
    private readonly IFileFindingRepository _repository;
    private readonly IStorageService _storage;
    private readonly IValidator<FileFinding> _validator;

    private const int BatchSize = 1000;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly IRejectedRowRepository _rejectedRowRepository;

    public IngestionService(
       ILogger<IngestionService> logger,
       IFileFindingRepository repository,
       IStorageService storage,
       IValidator<FileFinding> validator,
       IIngestionJobAuditRepository jobAuditRepository,
       IRejectedRowRepository rejectedRowRepository)
    {
        _logger = logger;
        _repository = repository;
        _storage = storage;
        _validator = validator;
        _jobAuditRepository = jobAuditRepository;
        _rejectedRowRepository = rejectedRowRepository;
    }

    public async Task<IngestionUploadResponse> ProcessAsync(IFormFile file)
    {
        var startedAtUtc = DateTime.UtcNow;
        var jobId = IngestionJobIdGenerator.Generate();

        if (file == null || file.Length == 0)
        {
            return new IngestionUploadResponse
            {
                JobId = jobId,
                Status = IngestionJobStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTime.UtcNow,
                Message = "Invalid file. Uploaded file is empty or missing."
            };
        }

        var uploadedBy = "system";
        var loadTime = DateTime.UtcNow;
        var inboundFileName = file.FileName;

        var response = new IngestionUploadResponse
        {
            JobId = jobId,
            InboundFileName = inboundFileName,
            StartedAtUtc = startedAtUtc,
            Status = IngestionJobStatus.Started
        };

        var jobAudit = new IngestionJobAudit
        {
            JobId = jobId,
            InboundFileName = inboundFileName,
            UserName = uploadedBy,
            StartTimestampUtc = startedAtUtc,
            Status = IngestionJobStatus.Started
        };

        _jobAuditRepository.Add(jobAudit);

        try
        {
            _logger.LogInformation(
                "Ingestion started. JobId: {JobId}, FileName: {FileName}",
                jobId,
                inboundFileName);

            var archiveKey = IngestionArchivePathBuilder.BuildOriginalFilePath(
                jobId,
                inboundFileName,
                startedAtUtc);

            await _storage.UploadAsync(archiveKey, file.OpenReadStream());
            response.ArchivedFilePath = archiveKey;

            List<FileFinding> findings;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            using var stream = file.OpenReadStream();

            if (ext == ".xlsx")
            {
                findings = ParseExcel(
                    stream,
                    jobId,
                    inboundFileName,
                    uploadedBy,
                    loadTime,
                    response.RejectedRows);
            }
            else if (ext == ".csv")
            {
                findings = ParseCsv(
                    stream,
                    jobId,
                    inboundFileName,
                    uploadedBy,
                    loadTime,
                    response.RejectedRows);
            }
            else
            {
                response.Status = IngestionJobStatus.Failed;
                response.CompletedAtUtc = DateTime.UtcNow;
                response.Message = "Unsupported file format. Only .csv and .xlsx files are supported.";
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);

                UpdateJobAudit(jobAudit, response);

                return response;
            }

            response.TotalRecords = findings.Count;
            response.RejectCount = findings.Count(x => !x.IsValid);
            response.SuccessCount = findings.Count(x => x.IsValid);

            PersistRejectedRows(
                jobId,
                inboundFileName,
                response.RejectedRows);

            var validFindings = findings
                .Where(x => x.IsValid)
                .ToList();

            foreach (var batch in validFindings.Chunk(BatchSize))
            {
                _repository.AddRange(batch.ToList());

                _logger.LogInformation(
                    "Ingestion batch saved. JobId: {JobId}, BatchSize: {BatchSize}",
                    jobId,
                    batch.Length);
            }

            response.Status = DetermineFinalStatus(response.SuccessCount, response.RejectCount);
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = BuildResponseMessage(
                response.Status,
                response.SuccessCount,
                response.RejectCount);

            response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);

            UpdateJobAudit(jobAudit, response);

            _logger.LogInformation(
                "Ingestion completed. JobId: {JobId}, Status: {Status}, Total: {Total}, Success: {Success}, Rejected: {Rejected}",
                jobId,
                response.Status,
                response.TotalRecords,
                response.SuccessCount,
                response.RejectCount);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ingestion failed. JobId: {JobId}, FileName: {FileName}",
                jobId,
                inboundFileName);

            response.Status = IngestionJobStatus.Failed;
            response.CompletedAtUtc = DateTime.UtcNow;
            response.Message = $"Ingestion failed: {ex.Message}";

            try
            {
                response.ProcessingSummaryPath = await StoreProcessingSummaryAsync(response);
            }
            catch (Exception summaryEx)
            {
                _logger.LogError(
                    summaryEx,
                    "Failed to store processing summary. JobId: {JobId}",
                    jobId);
            }

            try
            {
                UpdateJobAudit(jobAudit, response, ex.Message);
            }
            catch (Exception auditEx)
            {
                _logger.LogError(
                    auditEx,
                    "Failed to update ingestion job audit. JobId: {JobId}",
                    jobId);
            }

            return response;
        }
    }


    private List<FileFinding> ParseExcel(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        List<RejectedRowSummary> rejectedRows)
    {
        var findings = new List<FileFinding>();

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        var headerMap = BuildExcelHeaderMap(sheet);
        ValidateHeaders(headerMap.Keys.ToList());

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNumber = row.RowNumber();

            var finding = new FileFinding
            {
                Id = Guid.NewGuid(),

                FindingFileName = GetExcelValue(row, headerMap, InboundLayoutColumns.FindingFileName),
                FindingFileFormat = GetExcelValue(row, headerMap, InboundLayoutColumns.FindingFileFormat),
                CurrentFileLocation = GetExcelValue(row, headerMap, InboundLayoutColumns.CurrentFileLocation),
                FindingType = GetExcelValue(row, headerMap, InboundLayoutColumns.FindingType),
                OriginatingDataSystem = GetExcelValue(row, headerMap, InboundLayoutColumns.OriginatingDataSystem),
                OriginatingVendorTool = GetExcelValue(row, headerMap, InboundLayoutColumns.OriginatingVendorTool),
                OriginalFileLocation = GetExcelValue(row, headerMap, InboundLayoutColumns.OriginalFileLocation),
                QuarantineDate = TryParseNullableDate(GetExcelValue(row, headerMap, InboundLayoutColumns.QuarantineDate)),

                Status = FileStatus.Loaded,

                IngestionId = jobId,
                InboundFileName = inboundFileName,
                UploadedBy = uploadedBy,
                LoadDate = loadTime,
                UpdatedDate = loadTime
            };

            MapApprovedFieldsToExistingPocFields(finding);
            ApplyValidation(finding, rowNumber, rejectedRows);

            findings.Add(finding);
        }

        return findings;
    }

    private List<FileFinding> ParseCsv(
        Stream stream,
        string jobId,
        string inboundFileName,
        string uploadedBy,
        DateTime loadTime,
        List<RejectedRowSummary> rejectedRows)
    {
        var findings = new List<FileFinding>();

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();

        var headers = csv.HeaderRecord?.ToList() ?? new List<string>();
        ValidateHeaders(headers);

        while (csv.Read())
        {
            var rowNumber = csv.Context.Parser.Row;

            var finding = new FileFinding
            {
                Id = Guid.NewGuid(),

                FindingFileName = GetCsvValue(csv, InboundLayoutColumns.FindingFileName),
                FindingFileFormat = GetCsvValue(csv, InboundLayoutColumns.FindingFileFormat),
                CurrentFileLocation = GetCsvValue(csv, InboundLayoutColumns.CurrentFileLocation),
                FindingType = GetCsvValue(csv, InboundLayoutColumns.FindingType),
                OriginatingDataSystem = GetCsvValue(csv, InboundLayoutColumns.OriginatingDataSystem),
                OriginatingVendorTool = GetCsvValue(csv, InboundLayoutColumns.OriginatingVendorTool),
                OriginalFileLocation = GetCsvValue(csv, InboundLayoutColumns.OriginalFileLocation),
                QuarantineDate = TryParseNullableDate(GetCsvValue(csv, InboundLayoutColumns.QuarantineDate)),

                Status = FileStatus.Loaded,

                IngestionId = jobId,
                InboundFileName = inboundFileName,
                UploadedBy = uploadedBy,
                LoadDate = loadTime,
                UpdatedDate = loadTime
            };

            MapApprovedFieldsToExistingPocFields(finding);
            ApplyValidation(finding, rowNumber, rejectedRows);

            findings.Add(finding);
        }

        return findings;
    }

    private void ApplyValidation(
        FileFinding finding,
        int rowNumber,
        List<RejectedRowSummary> rejectedRows)
    {
        var result = _validator.Validate(finding);

        if (!result.IsValid)
        {
            finding.IsValid = false;
            finding.Status = FileStatus.Failed;
            finding.ErrorReason = string.Join(", ", result.Errors.Select(e => e.ErrorMessage));

            foreach (var error in result.Errors)
            {
                rejectedRows.Add(new RejectedRowSummary
                {
                    RowNumber = rowNumber,
                    FieldName = error.PropertyName,
                    RejectedValue = GetRejectedValue(finding, error.PropertyName),
                    ErrorReason = error.ErrorMessage
                });
            }

            return;
        }

        finding.IsValid = true;
        finding.ErrorReason = string.Empty;
    }

    private IngestionJobStatus DetermineFinalStatus(int successCount, int rejectCount)
    {
        if (successCount > 0 && rejectCount == 0)
            return IngestionJobStatus.Success;

        if (successCount > 0 && rejectCount > 0)
            return IngestionJobStatus.PartialSuccess;

        return IngestionJobStatus.Failed;
    }

    private string BuildResponseMessage(
        IngestionJobStatus status,
        int successCount,
        int rejectCount)
    {
        return status switch
        {
            IngestionJobStatus.Success =>
                "File processed successfully.",

            IngestionJobStatus.PartialSuccess =>
                $"File processed with partial success. Success: {successCount}, Rejected: {rejectCount}.",

            IngestionJobStatus.Failed =>
                "File processing failed. No valid records were ingested.",

            _ => "File processing completed."
        };
    }

    private string? GetRejectedValue(FileFinding finding, string propertyName)
    {
        return propertyName switch
        {
            nameof(FileFinding.FindingFileName) => finding.FindingFileName,
            nameof(FileFinding.FindingFileFormat) => finding.FindingFileFormat,
            nameof(FileFinding.CurrentFileLocation) => finding.CurrentFileLocation,
            nameof(FileFinding.FindingType) => finding.FindingType,
            nameof(FileFinding.OriginatingDataSystem) => finding.OriginatingDataSystem,
            nameof(FileFinding.OriginatingVendorTool) => finding.OriginatingVendorTool,
            nameof(FileFinding.OriginalFileLocation) => finding.OriginalFileLocation,
            nameof(FileFinding.QuarantineDate) => finding.QuarantineDate?.ToString("yyyy-MM-dd"),
            _ => null
        };
    }

    private void ValidateHeaders(List<string> headers)
    {
        var normalizedHeaders = headers
            .Select(NormalizeHeader)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = InboundLayoutColumns.RequiredColumns
            .Where(required => !normalizedHeaders.Contains(NormalizeHeader(required)))
            .ToList();

        if (missing.Any())
            throw new Exception($"Missing required columns: {string.Join(", ", missing)}");
    }

    private Dictionary<string, int> BuildExcelHeaderMap(IXLWorksheet sheet)
    {
        return sheet.Row(1)
            .CellsUsed()
            .ToDictionary(
                cell => NormalizeHeader(cell.GetString()),
                cell => cell.Address.ColumnNumber,
                StringComparer.OrdinalIgnoreCase);
    }

    private string GetExcelValue(
        IXLRow row,
        Dictionary<string, int> headerMap,
        string columnName)
    {
        var normalizedColumnName = NormalizeHeader(columnName);

        if (!headerMap.TryGetValue(normalizedColumnName, out var columnNumber))
            return string.Empty;

        return row.Cell(columnNumber).GetString()?.Trim() ?? string.Empty;
    }

    private string GetCsvValue(CsvReader csv, string columnName)
    {
        try
        {
            return csv.GetField(columnName)?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private DateTime? TryParseNullableDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            return parsedDate;

        return null;
    }

    private string NormalizeHeader(string value)
    {
        return value
            .Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .ToLowerInvariant();
    }

    private void MapApprovedFieldsToExistingPocFields(FileFinding finding)
    {
        finding.FileName = finding.FindingFileName;
        finding.FilePath = finding.CurrentFileLocation;
        finding.SourceSystem = finding.OriginatingDataSystem;
    }

    private async Task<string> StoreProcessingSummaryAsync(IngestionUploadResponse response)
    {
        var summary = new ProcessingSummaryArtifact
        {
            JobId = response.JobId,
            InboundFileName = response.InboundFileName,
            ProcessingStartTimeUtc = response.StartedAtUtc,
            ProcessingEndTimeUtc = response.CompletedAtUtc,
            TotalRowsProcessed = response.TotalRecords,
            SuccessfulRows = response.SuccessCount,
            FailedRows = response.RejectCount,
            FinalJobStatus = response.Status,
            Message = response.Message,
            ArchivedFilePath = response.ArchivedFilePath,
            RejectedRows = response.RejectedRows
        };

        var json = JsonSerializer.Serialize(
            summary,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

        var summaryKey = IngestionArchivePathBuilder.BuildProcessingSummaryPath(
            response.JobId,
            response.StartedAtUtc);

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
        audit.TotalRecords = response.TotalRecords;
        audit.SuccessCount = response.SuccessCount;
        audit.RejectCount = response.RejectCount;
        audit.Status = response.Status;
        audit.ErrorMessage = errorMessage;
        audit.ArchivedFilePath = response.ArchivedFilePath;
        audit.ProcessingSummaryPath = response.ProcessingSummaryPath;

        _jobAuditRepository.Update(audit);
    }

    private void PersistRejectedRows(
    string jobId,
    string inboundFileName,
    List<RejectedRowSummary> rejectedRows)
    {
        if (rejectedRows == null || rejectedRows.Count == 0)
            return;

        var rejectedRowDetails = rejectedRows
            .Select(row => new RejectedRowDetail
            {
                JobId = jobId,
                InboundFileName = inboundFileName,
                RowNumber = row.RowNumber,
                FieldName = row.FieldName,
                RejectedValue = row.RejectedValue,
                ErrorReason = row.ErrorReason,
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();

        _rejectedRowRepository.AddRange(rejectedRowDetails);
    }
}