namespace RemediationTool.Application.Services;

public static class IngestionJobIdGenerator
{
    public static string Generate()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var randomPart = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return $"ING-{timestamp}-{randomPart}";
    }
}