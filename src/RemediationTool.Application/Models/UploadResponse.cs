using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Models;

/// <summary>
/// Response returned by the Upload API (POST /api/upload).
/// Contains the ReportUID and S3 paths.
/// Ingestion status is tracked separately via the Ingestion API.
/// </summary>
public class UploadResponse
{
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Unique ID for this report upload.
    /// Format: ING-{yyyyMMdd}-{HHmmss}-{random8}
    /// Used as S3 folder name and DynamoDB primary key.
    /// </summary>
    public string ReportUid { get; set; } = string.Empty;

    /// <summary>Alias for ReportUid — backward compat.</summary>
    public string JobId { get; set; } = string.Empty;

    public string InboundFileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    /// <summary>S3 folder prefix: {yyyy}/{MM}/{reportUid}/</summary>
    public string S3FolderPath { get; set; } = string.Empty;

    /// <summary>Full S3 key of the uploaded source file.</summary>
    public string? SourceFilePath { get; set; }

    /// <summary>Full S3 key of the report-metadata.json.</summary>
    public string? MetadataJsonPath { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    public IngestionJobStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;
}