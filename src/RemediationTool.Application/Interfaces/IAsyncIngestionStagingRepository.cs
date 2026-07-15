using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

/// <summary>
/// Optional asynchronous staging contract used by high-volume ingestion.
/// The synchronous staging interface remains available for existing callers.
/// </summary>
public interface IAsyncIngestionStagingRepository
{
    Task SaveValidFindingsAsync(
        string jobId,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default);

    Task<List<FileFinding>> GetValidFindingsAfterAsync(
        string jobId,
        int lastProcessedRecordCount,
        CancellationToken cancellationToken = default);

    Task<int> CountByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task DeleteByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
