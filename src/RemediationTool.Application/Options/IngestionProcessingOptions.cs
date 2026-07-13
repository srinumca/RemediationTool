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

    /// <summary>
    /// Existing compatibility setting retained for deployments that already use it.
    /// New Phase 2 code prefers DynamoDbWriteConcurrency when it is greater than zero.
    /// </summary>
    public int DynamoDbMaxConcurrentBatchWrites { get; set; } = 4;

    /// <summary>
    /// Enables fully awaited 25-item DynamoDB writes with bounded concurrency.
    /// Enabled by default to preserve the bounded-write behavior already delivered
    /// by the previously merged ingestion optimization PRs.
    /// </summary>
    public bool EnableBoundedDynamoDbConcurrency { get; set; } = true;

    /// <summary>
    /// Maximum concurrent DynamoDB BatchWriteItem requests for findings, rejected
    /// rows and staging records. Values are clamped to 1..16.
    /// </summary>
    public int DynamoDbWriteConcurrency { get; set; } = 4;

    /// <summary>
    /// Number of rejected rows converted and submitted to the repository at one time.
    /// Repository implementations still split these into DynamoDB's 25-item requests.
    /// </summary>
    public int RejectedRowBatchSize { get; set; } = 5000;

    /// <summary>
    /// Enables direct S3 streaming for CSV and temporary seekable files for XLSX
    /// and Parquet operations. Disabled by default until environment load testing
    /// confirms equivalent behavior.
    /// </summary>
    public bool EnableHighVolumeStreaming { get; set; } = false;

    /// <summary>
    /// Keeps the existing buffered/synchronous path available if an async or streaming
    /// implementation is not available. This remains enabled through production rollout.
    /// </summary>
    public bool LegacyFallbackEnabled { get; set; } = true;

    public bool EnableParquetWorkingFile { get; set; } = true;

    /// <summary>
    /// Confirms that the uploaded working file exists before final findings are persisted.
    /// </summary>
    public bool ValidateWorkingFileAfterWrite { get; set; } = true;

    /// <summary>
    /// Uses a verified Parquet working file as the normal resume source and avoids
    /// duplicating every valid finding into the staging table. Disabled by default
    /// so the existing dual-store recovery behavior remains unchanged until the
    /// Parquet-first path is validated in the target AWS environment.
    /// </summary>
    public bool UseParquetAsPrimaryResumeStore { get; set; } = false;

    /// <summary>
    /// Preserves the existing staging-table recovery path when Parquet is disabled
    /// or cannot be created. Disable only after production validation.
    /// </summary>
    public bool LegacyStagingFallbackEnabled { get; set; } = true;

    /// <summary>
    /// Controls how often job-audit progress is persisted. Checkpoints are still
    /// written after every successful application batch. A value of 1 preserves
    /// the existing status-polling behavior.
    /// </summary>
    public int JobAuditProgressUpdateIntervalBatches { get; set; } = 1;

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

    public int ResolveDynamoDbWriteConcurrency()
    {
        var configured = DynamoDbWriteConcurrency > 0
            ? DynamoDbWriteConcurrency
            : DynamoDbMaxConcurrentBatchWrites;

        return Math.Clamp(configured, 1, 16);
    }

    public int ResolveRejectedRowBatchSize()
        => Math.Clamp(RejectedRowBatchSize, 25, MaxBatchSize > 0 ? MaxBatchSize : 10000);
}
