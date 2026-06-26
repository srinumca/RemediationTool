namespace RemediationTool.Application.Services;

/// <summary>
/// Builds all S3 paths for an EDG report upload.
///
/// S3 folder structure:
///   gfr-edg-reports/{yyyy}/{MM}/{reportUid}/
///       ├── {originalFileName}       ← original uploaded CSV/XLSX
///       ├── report-metadata.json     ← ingestion metadata JSON (same folder)
///       └── working/{name}.parquet   ← Parquet resume file (subfolder)
/// </summary>
public static class IngestionArchivePathBuilder
{
    private const string MetadataFileName = "report-metadata.json";

    /// <summary>
    /// Full S3 folder prefix for this report.
    /// Example: 2026/06/ING-20260616-153012-A1B2C3D4/
    /// Stored in DynamoDB to link S3 location to DynamoDB record.
    /// </summary>
    public static string BuildFolderPrefix(string reportUid, DateTime uploadedAtUtc)
        => $"{uploadedAtUtc:yyyy}/{uploadedAtUtc:MM}/{reportUid}/";

    /// <summary>
    /// Full S3 key for the original uploaded source file.
    /// Example: 2026/06/ING-20260616-153012-A1B2C3D4/Sample_SMB_GFR_Demo.csv
    /// </summary>
    public static string BuildOriginalFilePath(
        string reportUid, string inboundFileName, DateTime uploadedAtUtc)
    {
        var safeFileName = SanitizeFileName(inboundFileName);
        return $"{uploadedAtUtc:yyyy}/{uploadedAtUtc:MM}/{reportUid}/{safeFileName}";
    }

    /// <summary>
    /// Full S3 key for the report-metadata.json (same folder as source file).
    /// Example: 2026/06/ING-20260616-153012-A1B2C3D4/report-metadata.json
    /// </summary>
    public static string BuildProcessingSummaryPath(string reportUid, DateTime uploadedAtUtc)
        => $"{uploadedAtUtc:yyyy}/{uploadedAtUtc:MM}/{reportUid}/{MetadataFileName}";

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "uploaded-file";
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }
}