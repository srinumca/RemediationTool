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
    /// <summary>
    /// Compatibility overload for existing synchronous staging callers and tests.
    /// </summary>
    public static Task<IngestionResumeStoreResult> PrepareAsync(
        IngestionProcessingOptions options,
        int validRecordCount,
        Func<Task> createParquetAsync,
        Action writeStaging,
        Action clearParquetMetadata)
    {
        ArgumentNullException.ThrowIfNull(createParquetAsync);
        ArgumentNullException.ThrowIfNull(writeStaging);

        return PrepareAsync(
            options,
            validRecordCount,
            _ => createParquetAsync(),
            _ =>
            {
                writeStaging();
                return Task.CompletedTask;
            },
            clearParquetMetadata,
            CancellationToken.None);
    }

    public static async Task<IngestionResumeStoreResult> PrepareAsync(
        IngestionProcessingOptions options,
        int validRecordCount,
        Func<CancellationToken, Task> createParquetAsync,
        Func<CancellationToken, Task> writeStagingAsync,
        Action clearParquetMetadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createParquetAsync);
        ArgumentNullException.ThrowIfNull(writeStagingAsync);
        ArgumentNullException.ThrowIfNull(clearParquetMetadata);

        cancellationToken.ThrowIfCancellationRequested();

        if (validRecordCount <= 0)
            return new IngestionResumeStoreResult(false, false, null);

        var parquetReady = false;
        Exception? parquetFailure = null;

        if (options.EnableParquetWorkingFile)
        {
            try
            {
                await createParquetAsync(cancellationToken);
                parquetReady = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
            await writeStagingAsync(cancellationToken);
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

        await writeStagingAsync(cancellationToken);
        return new IngestionResumeStoreResult(false, true, parquetFailure);
    }
}
