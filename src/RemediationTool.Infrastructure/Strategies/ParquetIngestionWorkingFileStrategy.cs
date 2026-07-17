using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Serialization;
using RemediationTool.Application.Constants;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Domain.Entities;
using RemediationTool.Infrastructure.Storage;

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

        var workingFilePath = IngestionWorkingFilePathBuilder.BuildParquetPath(
            jobId,
            inboundFileName,
            DateTime.UtcNow);
        var rowGroupSize = Math.Max(1, _options.ParquetRowGroupSize);
        var parquetOptions = new ParquetOptions { RowGroupSize = rowGroupSize };

        _logger.LogInformation(
            "[PARQUET_WRITE_START] JobId:{JobId}, Path:{Path}, Records:{Records}, RowGroupSize:{RowGroupSize}",
            jobId,
            workingFilePath,
            validFindings.Count,
            rowGroupSize);

        await using Stream parquetStream = _options.EnableHighVolumeStreaming
            ? TemporarySeekableStream.Create()
            : new MemoryStream();

        await ParquetSerializer.SerializeAsync(
            validFindings.Select(ToParquetRow),
            parquetStream,
            parquetOptions,
            cancellationToken: cancellationToken);

        if (parquetStream.Length == 0 && validFindings.Count > 0)
        {
            throw new InvalidDataException(
                $"Parquet serialization produced an empty working file for job {jobId} with {validFindings.Count} valid records.");
        }

        parquetStream.Position = 0;
        await _storage.UploadAsync(workingFilePath, parquetStream, cancellationToken);

        if (_options.ValidateWorkingFileAfterWrite
            && !await _storage.ExistsAsync(workingFilePath, cancellationToken))
        {
            throw new IOException(
                $"Parquet working file verification failed for job {jobId}. The uploaded object was not found at '{workingFilePath}'.");
        }

        _logger.LogInformation(
            "[PARQUET_WRITE_COMPLETE] JobId:{JobId}, Path:{Path}, Records:{Records}, Bytes:{Bytes}, Verified:{Verified}",
            jobId,
            workingFilePath,
            validFindings.Count,
            parquetStream.Length,
            _options.ValidateWorkingFileAfterWrite);

        return new IngestionWorkingFileResult
        {
            Format = Format,
            Path = workingFilePath,
            RecordCount = validFindings.Count
        };
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

    private static string? ToText(DateTime? value)
        => value.HasValue ? value.Value.ToUniversalTime().ToString("O") : null;

    private static string ToText(DateTime value)
        => value.ToUniversalTime().ToString("O");

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
