using RemediationTool.Application.Interfaces;
using RemediationTool.Application.Options;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Services;

/// <summary>
/// Selects asynchronous high-volume implementations when available and keeps
/// the existing synchronous/buffered contracts as an explicit legacy fallback.
/// </summary>
internal static class IngestionAsyncIo
{
    public static async Task PersistFindingsAsync(
        IFileFindingRepository repository,
        IReadOnlyList<FileFinding> findings,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncFileFindingRepository asyncRepository)
        {
            await asyncRepository.AddRangeAsync(findings, cancellationToken);
            return;
        }

        EnsureLegacyFallback(options, "finding repository");
        repository.AddRange(findings);
    }

    public static async Task PersistRejectedRowsAsync(
        IRejectedRowRepository repository,
        IReadOnlyList<RejectedRowDetail> rejectedRows,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncRejectedRowRepository asyncRepository)
        {
            await asyncRepository.AddRangeAsync(rejectedRows, cancellationToken);
            return;
        }

        EnsureLegacyFallback(options, "rejected-row repository");
        repository.AddRange(
            rejectedRows as List<RejectedRowDetail>
            ?? rejectedRows.ToList());
    }

    public static async Task SaveStagingAsync(
        IIngestionStagingRepository repository,
        string jobId,
        IReadOnlyList<FileFinding> findings,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncIngestionStagingRepository asyncRepository)
        {
            await asyncRepository.SaveValidFindingsAsync(
                jobId,
                findings,
                cancellationToken);
            return;
        }

        EnsureLegacyFallback(options, "staging repository");
        repository.SaveValidFindings(
            jobId,
            findings as List<FileFinding>
            ?? findings.ToList());
    }

    public static async Task<int> CountStagedAsync(
        IIngestionStagingRepository repository,
        string jobId,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncIngestionStagingRepository asyncRepository)
            return await asyncRepository.CountByJobIdAsync(jobId, cancellationToken);

        EnsureLegacyFallback(options, "staging repository");
        return repository.CountByJobId(jobId);
    }

    public static async Task<List<FileFinding>> ReadStagedAfterAsync(
        IIngestionStagingRepository repository,
        string jobId,
        int lastProcessedRecordCount,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncIngestionStagingRepository asyncRepository)
        {
            return await asyncRepository.GetValidFindingsAfterAsync(
                jobId,
                lastProcessedRecordCount,
                cancellationToken);
        }

        EnsureLegacyFallback(options, "staging repository");
        return repository.GetValidFindingsAfter(jobId, lastProcessedRecordCount);
    }

    public static async Task DeleteStagingAsync(
        IIngestionStagingRepository repository,
        string jobId,
        IngestionProcessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (repository is IAsyncIngestionStagingRepository asyncRepository)
        {
            await asyncRepository.DeleteByJobIdAsync(jobId, cancellationToken);
            return;
        }

        EnsureLegacyFallback(options, "staging repository");
        repository.DeleteByJobId(jobId);
    }

    public static async Task<Stream> OpenSourceReadAsync(
        IStorageService storage,
        IngestionProcessingOptions options,
        string key,
        string extension,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options.EnableHighVolumeStreaming
            && storage is IStreamingStorageService streamingStorage)
        {
            return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                ? await streamingStorage.OpenReadAsync(key, cancellationToken)
                : await streamingStorage.OpenSeekableReadAsync(key, cancellationToken);
        }

        if (options.EnableHighVolumeStreaming)
            EnsureLegacyFallback(options, "streaming storage");

        return await storage.DownloadAsync(key, cancellationToken);
    }

    private static void EnsureLegacyFallback(
        IngestionProcessingOptions options,
        string component)
    {
        if (!options.LegacyFallbackEnabled)
        {
            throw new InvalidOperationException(
                $"Phase 2 high-volume I/O requires an asynchronous {component}, but no compatible implementation is registered and legacy fallback is disabled.");
        }
    }
}
