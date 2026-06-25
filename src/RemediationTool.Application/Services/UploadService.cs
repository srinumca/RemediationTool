using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Models;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Services;

/// <summary>
/// Upload Service — handles ONLY the file upload step.
///
/// Responsibilities:
///   1. Validate the uploaded file (size, extension)
///   2. Generate a unique ReportUID
///   3. Save the file to S3: gfr-edg-reports/{yyyy}/{MM}/{reportUid}/filename.csv
///   4. Save the metadata JSON to S3: gfr-edg-reports/{yyyy}/{MM}/{reportUid}/report-metadata.json
///   5. Create a report record in DynamoDB (gfr-file-metadata-dev) with status = NotYetStarted
///   6. Return 202 immediately with the ReportUID
///
/// Does NOT parse rows. Does NOT validate row data.
/// That is IngestionService.IngestAsync(reportUid)'s job.
/// </summary>
public class UploadService
{
    private readonly IStorageService _storage;
    private readonly IIngestionJobAuditRepository _jobAuditRepository;
    private readonly ILogger<UploadService> _logger;

    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB for large files
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

    /// <summary>
    /// Validates and uploads the file to S3, creates DynamoDB record.
    /// Returns immediately — does NOT start row ingestion.
    /// </summary>
    public async Task<UploadResponse> UploadAsync(IFormFile file)
    {
        // Step 1: Validate file
        ValidateFile(file);

        var uploadedAtUtc = DateTime.UtcNow;
        var reportUid = IngestionJobIdGenerator.Generate();
        var inboundFileName = file.FileName;
        var fileSizeBytes = file.Length;
        var fileFormat = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();

        // Step 2: Build S3 paths
        var s3FolderPath = IngestionArchivePathBuilder.BuildFolderPrefix(reportUid, uploadedAtUtc);
        var sourceFilePath = IngestionArchivePathBuilder.BuildOriginalFilePath(reportUid, inboundFileName, uploadedAtUtc);
        var metadataPath = IngestionArchivePathBuilder.BuildProcessingSummaryPath(reportUid, uploadedAtUtc);

        _logger.LogInformation(
            "Upload started. ReportUID: {ReportUid}, File: {File}, Size: {Size} bytes, S3Folder: {Folder}",
            reportUid, inboundFileName, fileSizeBytes, s3FolderPath);

        // Step 3: Save source file to S3
        using var fileStream = file.OpenReadStream();
        await _storage.UploadAsync(sourceFilePath, fileStream);

        _logger.LogInformation(
            "Source file saved to S3. ReportUID: {ReportUid}, Path: {Path}",
            reportUid, sourceFilePath);

        // Step 4: Save metadata JSON to S3 (same folder)
        var metadata = BuildMetadata(
            reportUid, inboundFileName, fileSizeBytes,
            fileFormat, s3FolderPath, sourceFilePath, uploadedAtUtc);

        await SaveMetadataJsonAsync(metadataPath, metadata);

        // Step 5: Create report record in DynamoDB with status = NotYetStarted
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
            "Report record created in DynamoDB. ReportUID: {ReportUid}, Status: {Status}",
            reportUid, jobAudit.Status);

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
            Message = $"File uploaded successfully. ReportUID: {reportUid}. Ingestion will begin shortly."
        };
    }

    /// <summary>
    /// Returns the current status of a report upload.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void ValidateFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            throw new InvalidDataException("Uploaded file is required and cannot be empty.");

        if (file.Length > MaxFileSizeBytes)
            throw new InvalidDataException(
                $"File size {file.Length / (1024 * 1024)}MB exceeds the maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)}MB.");

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext))
            throw new InvalidDataException("Uploaded file must have a valid extension.");

        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Unsupported file format '{ext}'. Only .csv and .xlsx files are allowed.");
    }

    private static object BuildMetadata(
        string reportUid, string fileName, long fileSize,
        string fileFormat, string s3Folder, string sourceFilePath,
        DateTime uploadedAt) => new
        {
            reportUid,
            originalFileName = fileName,
            fileSizeBytes = fileSize,
            fileFormat,
            s3FolderPath = s3Folder,
            sourceFilePath,
            uploadedBy = "system",
            uploadedAtUtc = uploadedAt,
            status = "Uploaded",
            ingestionStatus = "NotYetStarted"
        };

    private async Task SaveMetadataJsonAsync(string s3Key, object metadata)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            metadata,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await _storage.UploadAsync(s3Key, stream);
    }
}