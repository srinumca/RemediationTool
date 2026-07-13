using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using System.Text.Json;

namespace RemediationTool.Application.Services;

/// <summary>
/// Upload Service — handles only the source-file upload step.
/// </summary>
public class UploadService
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx" };

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IStorageService _storage;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly ILogger<UploadService> _logger;
    private readonly IngestionProcessingOptions _processingOptions;

    public UploadService(
        IStorageService storage,
        IIngestionJobAuditRepository jobAuditRepository,
        ILogger<UploadService> logger,
        IOptions<IngestionProcessingOptions> processingOptions)
    {
        _storage = storage;
        _jobAuditRepository = jobAuditRepository;
        _logger = logger;
        _processingOptions = processingOptions.Value;
    }

    public async Task<UploadResponse> UploadAsync(IFormFile file)
    {
        ValidateFile(file);

        var uploadedAtUtc = DateTime.UtcNow;
        var reportUid = IngestionJobIdGenerator.Generate();
        var inboundFileName = file.FileName;
        var fileSizeBytes = file.Length;
        var fileFormat = Path.GetExtension(inboundFileName)
            .TrimStart('.')
            .ToLowerInvariant();

        var s3FolderPath = IngestionArchivePathBuilder.BuildFolderPrefix(
            reportUid,
            uploadedAtUtc);
        var sourceFilePath = IngestionArchivePathBuilder.BuildOriginalFilePath(
            reportUid,
            inboundFileName,
            uploadedAtUtc);
        var metadataPath = IngestionArchivePathBuilder.BuildProcessingSummaryPath(
            reportUid,
            uploadedAtUtc);

        _logger.LogInformation(
            "[UPLOAD START] ReportUid: {ReportUid}, File: {File}, Size: {Size} bytes",
            reportUid,
            inboundFileName,
            fileSizeBytes);

        await using (var fileStream = file.OpenReadStream())
        {
            await _storage.UploadAsync(sourceFilePath, fileStream);
        }

        _logger.LogInformation(
            "[UPLOAD S3] ReportUid: {ReportUid} — file saved to S3. Key: {S3Key}",
            reportUid,
            sourceFilePath);

        await SaveMetadataJsonAsync(
            metadataPath,
            reportUid,
            inboundFileName,
            fileSizeBytes,
            fileFormat,
            s3FolderPath,
            sourceFilePath,
            uploadedAtUtc);

        var jobAudit = new IngestionJobAudit
        {
            ReportUid = reportUid,
            JobId = reportUid,
            InboundFileName = inboundFileName,
            FileSizeBytes = fileSizeBytes,
            FileFormat = fileFormat,
            S3FolderPath = s3FolderPath,
            SourceFilePath = sourceFilePath,
            MetadataJsonPath = metadataPath,
            ArchivedFilePath = sourceFilePath,
            ProcessingSummaryPath = metadataPath,
            UploadedBy = "system",
            UserName = "system",
            StartedBy = "system",
            StartTimestampUtc = uploadedAtUtc,
            Status = IngestionJobStatus.Started,
            TriggerType = "Manual",
            IngestionMode = "Full"
        };

        _jobAuditRepository.Add(jobAudit);

        _logger.LogInformation(
            "[UPLOAD DB] ReportUid: {ReportUid} — job record created in DynamoDB. Status: {Status}",
            reportUid,
            jobAudit.Status);

        _logger.LogInformation(
            "[UPLOAD COMPLETE] ReportUid: {ReportUid}, S3: {S3Path} — returning 202 Accepted.",
            reportUid,
            s3FolderPath);

        return new UploadResponse
        {
            IsSuccess = true,
            ReportUid = reportUid,
            JobId = reportUid,
            InboundFileName = inboundFileName,
            FileSizeBytes = fileSizeBytes,
            S3FolderPath = s3FolderPath,
            SourceFilePath = sourceFilePath,
            MetadataJsonPath = metadataPath,
            UploadedAtUtc = uploadedAtUtc,
            Status = IngestionJobStatus.Started,
            Message = $"File uploaded. ReportUid: {reportUid}. Ingestion will begin shortly."
        };
    }

    public UploadResponse? GetStatus(string reportUid)
    {
        if (string.IsNullOrWhiteSpace(reportUid))
            return null;

        var audit = _jobAuditRepository.GetByJobId(reportUid);
        if (audit == null)
            return null;

        return new UploadResponse
        {
            IsSuccess = true,
            ReportUid = audit.ReportUid,
            JobId = audit.JobId,
            InboundFileName = audit.InboundFileName,
            FileSizeBytes = audit.FileSizeBytes,
            S3FolderPath = audit.S3FolderPath,
            SourceFilePath = audit.SourceFilePath,
            MetadataJsonPath = audit.MetadataJsonPath,
            UploadedAtUtc = audit.StartTimestampUtc,
            Status = audit.Status,
            Message = audit.Status.ToString()
        };
    }

    private void ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("[UPLOAD REJECTED] Reason: file is null or empty.");
            throw new InvalidDataException("Uploaded file is required and cannot be empty.");
        }

        var maxFileSizeBytes = _processingOptions.MaxUploadFileSizeBytes;
        if (file.Length > maxFileSizeBytes)
        {
            _logger.LogWarning(
                "[UPLOAD REJECTED] FileName: {FileName} Reason: file size {Size} exceeds max {Max} bytes.",
                file.FileName,
                file.Length,
                maxFileSizeBytes);

            throw new InvalidDataException(
                $"Uploaded file size exceeds the allowed limit of {_processingOptions.MaxUploadFileSizeMb} MB.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            _logger.LogWarning(
                "[UPLOAD REJECTED] FileName: {FileName} Reason: unsupported extension '{Extension}'.",
                file.FileName,
                extension);

            throw new InvalidDataException(
                "Unsupported file format. Only .csv and .xlsx files are allowed.");
        }
    }

    private async Task SaveMetadataJsonAsync(
        string metadataPath,
        string reportUid,
        string inboundFileName,
        long fileSizeBytes,
        string fileFormat,
        string s3FolderPath,
        string sourceFilePath,
        DateTime uploadedAtUtc)
    {
        var metadata = new
        {
            ReportUid = reportUid,
            InboundFileName = inboundFileName,
            FileSizeBytes = fileSizeBytes,
            FileFormat = fileFormat,
            S3FolderPath = s3FolderPath,
            SourceFilePath = sourceFilePath,
            UploadedAtUtc = uploadedAtUtc,
            Status = "Uploaded"
        };

        await using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, metadata, MetadataJsonOptions);
        stream.Position = 0;
        await _storage.UploadAsync(metadataPath, stream);

        _logger.LogInformation(
            "[UPLOAD S3] ReportUid: {ReportUid} — initial metadata written to S3. Key: {Key}",
            reportUid,
            metadataPath);
    }
}
