namespace RemediationTool.Application.Services;

public static class IngestionArchivePathBuilder
{
    public static string BuildOriginalFilePath(string jobId, string inboundFileName, DateTime startedAtUtc)
    {
        var safeFileName = SanitizeFileName(inboundFileName);
        var datePath = startedAtUtc.ToString("yyyy/MM/dd");

        return $"ingestion/processed/{datePath}/{jobId}/original/{safeFileName}";
    }

    public static string BuildProcessingSummaryPath(string jobId, DateTime startedAtUtc)
    {
        var datePath = startedAtUtc.ToString("yyyy/MM/dd");

        return $"ingestion/processed/{datePath}/{jobId}/summary/processing-summary.json";
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "uploaded-file";

        var invalidChars = Path.GetInvalidFileNameChars();

        var cleaned = new string(
            fileName
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray());

        return cleaned.Trim();
    }
}