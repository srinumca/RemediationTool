using RemediationTool.Application.Options;

namespace RemediationTool.Application.Services;

internal readonly record struct IngestionResumeStoreResult(
    bool ParquetReady,
    bool StagingWritten,
    Exception? ParquetFailure);

/// <summary>
/// Chooses the durable resume store without changing ingestion validation,
/// target persistence, checkpoint, or response behavior.
/// </summary>
internal static class IngestionResumeStoreCoordinator
{
    public static async Task<IngestionResumeStoreResult> PrepareAsync(
        IngestionProcessingOptions options,
        int validRecordCount,
        Func<Task> createParquetAsync,
        Action writeStaging,
        Action clearParquetMetadata)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createParquetAsync);
        ArgumentNullException.ThrowIfNull(writeStaging);
        ArgumentNullException.ThrowIfNull(clearParquetMetadata);

        if (validRecordCount <= 0)
            return new IngestionResumeStoreResult(false, false, null);

        var parquetReady = false;
        Exception? parquetFailure = null;

        if (options.EnableParquetWorkingFile)
        {
            try
            {
                await createParquetAsync();
                parquetReady = true;
            }
            catch (Exception ex)
            {
                parquetFailure = ex;
                clearParquetMetadata();

                if (options.UseParquetAsPrimaryResumeStore
                    && !options.LegacyStagingFallbackEnabled)
                {
                    throw;
                }
            }
        }

        if (!options.UseParquetAsPrimaryResumeStore)
        {
            writeStaging();
            return new IngestionResumeStoreResult(parquetReady, true, parquetFailure);
        }

        if (parquetReady)
            return new IngestionResumeStoreResult(true, false, null);

        if (!options.LegacyStagingFallbackEnabled)
        {
            throw new InvalidOperationException(
                "No durable ingestion resume store is available. Parquet is unavailable and staging fallback is disabled.",
                parquetFailure);
        }

        writeStaging();
        return new IngestionResumeStoreResult(false, true, parquetFailure);
    }
}
