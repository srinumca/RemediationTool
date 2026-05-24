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
}