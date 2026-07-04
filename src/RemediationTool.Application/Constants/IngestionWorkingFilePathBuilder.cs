namespace RemediationTool.Application.Constants;

public static class IngestionWorkingFilePathBuilder
{
    public static string BuildParquetPath(
        string jobId,
        string inboundFileName,
        DateTime createdAtUtc)
    {
        var safeFileName = SanitizeFileNameWithoutExtension(inboundFileName);

        return Path.Combine(
            "ingestion-working",
            createdAtUtc.ToString("yyyy"),
            createdAtUtc.ToString("MM"),
            createdAtUtc.ToString("dd"),
            jobId,
            $"{safeFileName}.parquet")
            .Replace("\\", "/");
            }

    private static string SanitizeFileNameWithoutExtension(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            nameWithoutExtension = nameWithoutExtension.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(nameWithoutExtension)
            ? "ingestion-working"
            : nameWithoutExtension;
    }
}