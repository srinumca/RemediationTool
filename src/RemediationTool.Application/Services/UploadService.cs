using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;
using System.Text;
using System.Text.Json;

namespace RemediationTool.Application.Services;

/// <summary>
/// Upload Service — handles ONLY the file upload step.
/// Saves file to S3, creates DynamoDB record, returns 202 immediately.
/// Does NOT parse or validate rows — that is IngestionService.IngestAsync()'s job.
/// </summary>
public class UploadService
{
    private readonly IStorageService _storage;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly ILogger<UploadService> _logger;

    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB
    private static readonly string[] AllowedExtensions = { ".csv", ".xlsx" };

    public UploadService(
        IStorageService storage,
        IIngestionJobAuditRepository jobAuditRepository,
        ILogger<UploadService> logger)
    {
        _storage = storage;
        _jobAuditRepository = jobAuditRepository;
        _logger = logger;
    }

    public async Task<UploadResponse> UploadAsync(IFormFile file)
    {
        ValidateFile(file);

        var uploadedAtUtc = DateTime.UtcNow;
        var reportUid = IngestionJobIdGenerator.Generate();
        var inboundFileName = file.FileName;
        var fileSizeBytes = file.Length;
        var fileFormat = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();

        // Build S3 paths
        var s3FolderPath = IngestionArchivePathBuilder.BuildFolderPrefix(reportUid, uploadedAtUtc);
        var sourceFilePath = IngestionArchivePathBuilder.BuildOriginalFilePath(reportUid, inboundFileName, uploadedAtUtc);
        var metadataPath = IngestionArchivePathBuilder.BuildProcessingSummaryPath(reportUid, uploadedAtUtc);

        _logger.LogInformation(
            "Upload started. ReportUid: {ReportUid}, File: {File}, Size: {Size} bytes",
            reportUid, inboundFileName, fileSizeBytes);

        // Save source file to S3
        using var fileStream = file.OpenReadStream();
        await _storage.UploadAsync(sourceFilePath, fileStream);

        // Save metadata JSON to S3 (same folder)
        await SaveMetadataJsonAsync(metadataPath, reportUid, inboundFileName,
            fileSizeBytes, fileFormat, s3FolderPath, sourceFilePath, uploadedAtUtc);

        // Create DynamoDB record
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
            "Upload complete. ReportUid: {ReportUid}, S3: {S3Path}",
            reportUid, s3FolderPath);

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
        var audit = _jobAuditRepository.GetByJobId(reportUid);
        if (audit == null) return null;

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
            Message = $"Status: {audit.Status}"
        };
    }

    private static void ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new InvalidDataException("Uploaded file is required and cannot be empty.");
        if (file.Length > MaxFileSizeBytes)
            throw new InvalidDataException(
                $"File size exceeds the maximum {MaxFileSizeBytes / (1024 * 1024)}MB.");
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext))
            throw new InvalidDataException("File must have a valid extension.");
        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Only .csv and .xlsx files are allowed.");
    }

    private async Task SaveMetadataJsonAsync(
        string s3Key, string reportUid, string fileName,
        long fileSize, string fileFormat, string s3Folder,
        string sourceFilePath, DateTime uploadedAt)
    {
        var metadata = new
        {
            reportUid,
            originalFileName = fileName,
            fileSizeBytes = fileSize,
            fileFormat,
            s3FolderPath = s3Folder,
            sourceFilePath,
            uploadedBy = "system",
            uploadedAtUtc = uploadedAt,
            status = "Uploaded"
        };
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _storage.UploadAsync(s3Key, stream);
    }
}