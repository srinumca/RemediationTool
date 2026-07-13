namespace RemediationTool.Application.Options;

public class IngestionProcessingOptions
{
    public const string SectionName = "IngestionProcessing";

    /// <summary>
    /// Number of validated findings handled by one application-level persistence batch.
    /// DynamoDB still writes in groups of 25 internally; a larger outer batch reduces
    /// checkpoint, audit, and retry-pipeline overhead without changing record identity.
    /// </summary>
    public int BatchSize { get; set; } = 5000;

    public int MaxBatchSize { get; set; } = 10000;

    public int MinBatchSize { get; set; } = 100;

    public bool EnableBatchCheckpointing { get; set; } = true;

    public int MaxBatchPersistenceRetryCount { get; set; } = 3;

    public int BatchPersistenceRetryDelayMilliseconds { get; set; } = 1000;

    public bool EnableParquetWorkingFile { get; set; } = true;

    /// <summary>
    /// Confirms that the uploaded working file exists before final findings are persisted.
    /// </summary>
    public bool ValidateWorkingFileAfterWrite { get; set; } = true;

    /// <summary>
    /// Number of rows written to each Parquet row group. Larger row groups provide
    /// better compression and scan performance for high-volume ingestion datasets.
    /// </summary>
    public int ParquetRowGroupSize { get; set; } = 50000;

    /// <summary>
    /// Buffer used by StreamReader while parsing CSV uploads.
    /// </summary>
    public int CsvReaderBufferSize { get; set; } = 65536;

    /// <summary>
    /// Maximum inbound CSV/XLSX upload size in MB.
    /// This value is used by ASP.NET Core request/form limits and ingestion validation
    /// so large-file behavior remains consistent across environments.
    /// </summary>
    public int MaxUploadFileSizeMb { get; set; } = 500;

    public long MaxUploadFileSizeBytes => MaxUploadFileSizeMb * 1024L * 1024L;
}
