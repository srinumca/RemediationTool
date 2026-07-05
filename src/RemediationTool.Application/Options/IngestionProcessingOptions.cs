namespace RemediationTool.Application.Options;

public class IngestionProcessingOptions
{
    public const string SectionName = "IngestionProcessing";

    public int BatchSize { get; set; } = 1000;

    public int MaxBatchSize { get; set; } = 10000;

    public int MinBatchSize { get; set; } = 100;

    public bool EnableBatchCheckpointing { get; set; } = true;

    public int MaxBatchPersistenceRetryCount { get; set; } = 3;

    public int BatchPersistenceRetryDelayMilliseconds { get; set; } = 1000;

    public bool EnableParquetWorkingFile { get; set; } = true;

    public int ParquetRowGroupSize { get; set; } = 10000;

    /// <summary>
    /// Maximum inbound CSV/XLSX upload size in MB.
    /// This value is used by ASP.NET Core request/form limits and ingestion validation
    /// so large-file behavior remains consistent across environments.
    /// </summary>
    public int MaxUploadFileSizeMb { get; set; } = 500;

    public long MaxUploadFileSizeBytes => MaxUploadFileSizeMb * 1024L * 1024L;
}
