using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet.Serialization;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Strategies;

public class ParquetIngestionWorkingFileStrategy : IIngestionWorkingFileStrategy
{
    private readonly IStorageService _storage;
    private readonly IngestionProcessingOptions _options;
    private readonly ILogger<ParquetIngestionWorkingFileStrategy> _logger;

    public ParquetIngestionWorkingFileStrategy(
        IStorageService storage,
        IOptions<IngestionProcessingOptions> options,
        ILogger<ParquetIngestionWorkingFileStrategy> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public string Format => "Parquet";

    public async Task<IngestionWorkingFileResult> WriteAsync(
        string jobId,
        string inboundFileName,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        ArgumentNullException.ThrowIfNull(validFindings);

        var workingFilePath = IngestionWorkingFilePathBuilder.BuildParquetPath(jobId, inboundFileName, DateTime.UtcNow);
        var rowGroupSize = Math.Max(1, _options.ParquetRowGroupSize);
        var rows = validFindings.Select(ToParquetRow).ToList();

        _logger.LogInformation(
            "[PARQUET_WRITE_START] JobId:{JobId}, Path:{Path}, Records:{Records}, ConfiguredRowGroupSize:{RowGroupSize}",
            jobId, workingFilePath, rows.Count, rowGroupSize);

        await using var parquetStream = new MemoryStream();
        await ParquetSerializer.SerializeAsync(rows, parquetStream, cancellationToken: cancellationToken);

        parquetStream.Position = 0;
        await _storage.UploadAsync(workingFilePath, parquetStream);

        _logger.LogInformation(
            "[PARQUET_WRITE_COMPLETE] JobId:{JobId}, Path:{Path}, Records:{Records}",
            jobId, workingFilePath, rows.Count);

        return new IngestionWorkingFileResult
        {
            Format = Format,
            Path = workingFilePath,
            RecordCount = rows.Count
        };
    }

    public async Task<List<FileFinding>> ReadAfterAsync(
        string workingFilePath,
        int lastProcessedRecordCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingFilePath))
            throw new ArgumentException("Working file path is required.", nameof(workingFilePath));

        if (lastProcessedRecordCount < 0)
            throw new ArgumentOutOfRangeException(nameof(lastProcessedRecordCount));

        _logger.LogInformation(
            "[PARQUET_RESUME_READ_START] Path:{Path}, LastProcessedRecordCount:{LastProcessedRecordCount}",
            workingFilePath, lastProcessedRecordCount);

        await using var parquetStream = await _storage.DownloadAsync(workingFilePath);
        if (parquetStream.CanSeek)
            parquetStream.Position = 0;

        var rows = await ParquetSerializer.DeserializeAsync<ParquetFindingRow>(parquetStream, cancellationToken: cancellationToken);
        var remainingRows = rows.Skip(lastProcessedRecordCount).ToList();
        var findings = remainingRows.Select(ToFileFinding).ToList();

        _logger.LogInformation(
            "[PARQUET_RESUME_READ_COMPLETE] Path:{Path}, TotalRows:{TotalRows}, RemainingRecords:{RemainingRecords}",
            workingFilePath, rows.Count, findings.Count);

        return findings;
    }

    private static ParquetFindingRow ToParquetRow(FileFinding finding)
        => new()
        {
            Id = finding.Id.ToString(),
            RecordVersionId = finding.RecordVersionId,
            SourceRecordId = finding.SourceRecordId,
            IngestionJobId = finding.IngestionJobId,
            InboundFileName = finding.InboundFileName,
            UserName = finding.UserName,
            LoadDateUtc = ToText(finding.LoadDateUtc),
            LastUpdateDateUtc = ToText(finding.LastUpdateDateUtc),
            FindingFileName = finding.FindingFileName,
            FindingFileFormat = finding.FindingFileFormat,
            FindingFileSizeBytes = finding.FindingFileSizeBytes?.ToString(),
            CurrentFileLocation = finding.CurrentFileLocation,
            FindingType = finding.FindingType,
            DataSystem = finding.DataSystem,
            OriginatingDataSystem = finding.OriginatingDataSystem,
            OriginatingVendorTool = finding.OriginatingVendorTool,
            Status = finding.Status.ToString(),
            StatusColumnValue = finding.StatusColumnValue,
            ErrorCategory = finding.ErrorCategory,
            ErrorReason = finding.ErrorReason,
            LastModifiedDateUtc = ToText(finding.LastModifiedDateUtc),
            CreatedDateUtc = ToText(finding.CreatedDateUtc),
            LastAccessedDateUtc = ToText(finding.LastAccessedDateUtc),
            DetectionDateUtc = ToText(finding.DetectionDateUtc),
            SiteOwner = finding.SiteOwner,
            FileOwner = finding.FileOwner,
            BusinessUnit = finding.BusinessUnit,
            Division = finding.Division,
            Department = finding.Department,
            Region = finding.Region,
            Country = finding.Country,
            PolicyName = finding.PolicyName,
            PolicyId = finding.PolicyId,
            FindingReason = finding.FindingReason,
            RiskLevel = finding.RiskLevel,
            SensitivityLabel = finding.SensitivityLabel,
            RecommendedAction = finding.RecommendedAction,
            OriginalFileLocation = finding.OriginalFileLocation,
            QuarantineDateUtc = ToText(finding.QuarantineDateUtc),
            RestoredDateUtc = ToText(finding.RestoredDateUtc),
            ExceptionDateUtc = ToText(finding.ExceptionDateUtc),
            DeletedDateUtc = ToText(finding.DeletedDateUtc),
            RestorationTicketIdentifier = finding.RestorationTicketIdentifier,
            RestorationRequestorEmail = finding.RestorationRequestorEmail,
            RestorationComment = finding.RestorationComment
        };

    private static FileFinding ToFileFinding(ParquetFindingRow row)
    {
        var statusText = string.IsNullOrWhiteSpace(row.StatusColumnValue) ? row.Status : row.StatusColumnValue;

        var finding = new FileFinding
        {
            Id = Guid.TryParse(row.Id, out var id) ? id : Guid.NewGuid(),
            RecordVersionId = row.RecordVersionId ?? string.Empty,
            SourceRecordId = NullIfWhiteSpace(row.SourceRecordId),
            IngestionJobId = NullIfWhiteSpace(row.IngestionJobId),
            InboundFileName = row.InboundFileName ?? string.Empty,
            UserName = string.IsNullOrWhiteSpace(row.UserName) ? "System" : row.UserName,
            LoadDateUtc = ParseDate(row.LoadDateUtc) ?? DateTime.UtcNow,
            LastUpdateDateUtc = ParseDate(row.LastUpdateDateUtc) ?? DateTime.UtcNow,
            FindingFileName = row.FindingFileName ?? string.Empty,
            FindingFileFormat = row.FindingFileFormat ?? string.Empty,
            FindingFileSizeBytes = ParseLong(row.FindingFileSizeBytes),
            CurrentFileLocation = row.CurrentFileLocation ?? string.Empty,
            OriginatingDataSystem = row.OriginatingDataSystem ?? string.Empty,
            OriginatingVendorTool = row.OriginatingVendorTool ?? string.Empty,
            SourceSystemPlatform = NullIfWhiteSpace(row.DataSystem),
            ErrorCategory = NullIfWhiteSpace(row.ErrorCategory),
            LastModifiedDateUtc = ParseDate(row.LastModifiedDateUtc),
            CreatedDateUtc = ParseDate(row.CreatedDateUtc),
            LastAccessedDateUtc = ParseDate(row.LastAccessedDateUtc),
            DetectionDateUtc = ParseDate(row.DetectionDateUtc),
            SiteOwner = NullIfWhiteSpace(row.SiteOwner),
            FileOwner = NullIfWhiteSpace(row.FileOwner),
            BusinessUnit = NullIfWhiteSpace(row.BusinessUnit),
            Division = NullIfWhiteSpace(row.Division),
            Department = NullIfWhiteSpace(row.Department),
            Region = NullIfWhiteSpace(row.Region),
            Country = NullIfWhiteSpace(row.Country),
            PolicyName = NullIfWhiteSpace(row.PolicyName),
            PolicyId = NullIfWhiteSpace(row.PolicyId),
            FindingReason = NullIfWhiteSpace(row.FindingReason),
            RiskLevel = NullIfWhiteSpace(row.RiskLevel),
            SensitivityLabel = NullIfWhiteSpace(row.SensitivityLabel),
            RecommendedAction = NullIfWhiteSpace(row.RecommendedAction),
            OriginalFileLocation = NullIfWhiteSpace(row.OriginalFileLocation),
            QuarantineDateUtc = ParseDate(row.QuarantineDateUtc),
            RestoredDateUtc = ParseDate(row.RestoredDateUtc),
            ExceptionDateUtc = ParseDate(row.ExceptionDateUtc),
            DeletedDateUtc = ParseDate(row.DeletedDateUtc),
            RestorationTicketIdentifier = NullIfWhiteSpace(row.RestorationTicketIdentifier),
            RestorationRequestorEmail = NullIfWhiteSpace(row.RestorationRequestorEmail),
            RestorationComment = NullIfWhiteSpace(row.RestorationComment)
        };

        finding.FindingType = row.FindingType ?? string.Empty;
        finding.Status = FileFinding.ResolveStatusFromStoredValue(statusText);
        finding.StatusColumnValue = string.IsNullOrWhiteSpace(statusText) ? finding.Status.ToString() : statusText;
        finding.ErrorReason = row.ErrorReason ?? string.Empty;

        return finding;
    }

    private static string? ToText(DateTime? value)
        => value.HasValue ? value.Value.ToUniversalTime().ToString("O") : null;

    private static string ToText(DateTime value)
        => value.ToUniversalTime().ToString("O");

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static long? ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : null;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ParquetFindingRow
    {
        public string? Id { get; set; }
        public string? RecordVersionId { get; set; }
        public string? SourceRecordId { get; set; }
        public string? IngestionJobId { get; set; }
        public string? InboundFileName { get; set; }
        public string? UserName { get; set; }
        public string? LoadDateUtc { get; set; }
        public string? LastUpdateDateUtc { get; set; }
        public string? FindingFileName { get; set; }
        public string? FindingFileFormat { get; set; }
        public string? FindingFileSizeBytes { get; set; }
        public string? CurrentFileLocation { get; set; }
        public string? FindingType { get; set; }
        public string? DataSystem { get; set; }
        public string? OriginatingDataSystem { get; set; }
        public string? OriginatingVendorTool { get; set; }
        public string? Status { get; set; }
        public string? StatusColumnValue { get; set; }
        public string? ErrorCategory { get; set; }
        public string? ErrorReason { get; set; }
        public string? LastModifiedDateUtc { get; set; }
        public string? CreatedDateUtc { get; set; }
        public string? LastAccessedDateUtc { get; set; }
        public string? DetectionDateUtc { get; set; }
        public string? SiteOwner { get; set; }
        public string? FileOwner { get; set; }
        public string? BusinessUnit { get; set; }
        public string? Division { get; set; }
        public string? Department { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
        public string? PolicyName { get; set; }
        public string? PolicyId { get; set; }
        public string? FindingReason { get; set; }
        public string? RiskLevel { get; set; }
        public string? SensitivityLabel { get; set; }
        public string? RecommendedAction { get; set; }
        public string? OriginalFileLocation { get; set; }
        public string? QuarantineDateUtc { get; set; }
        public string? RestoredDateUtc { get; set; }
        public string? ExceptionDateUtc { get; set; }
        public string? DeletedDateUtc { get; set; }
        public string? RestorationTicketIdentifier { get; set; }
        public string? RestorationRequestorEmail { get; set; }
        public string? RestorationComment { get; set; }
    }
}
