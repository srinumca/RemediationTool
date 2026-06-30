namespace RemediationTool.Application.Services;

public static class IngestionJobIdGenerator
{
    /// <summary>
    /// Generates a unique ingestion job ID in the format "ING-yyyyMMdd-HHmmss-XXXXXXXX",
    /// </summary>
    /// <returns></returns>
    public static string Generate()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var randomPart = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return $"ING-{timestamp}-{randomPart}";
    }
}