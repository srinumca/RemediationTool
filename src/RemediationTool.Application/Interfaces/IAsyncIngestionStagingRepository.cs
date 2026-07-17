using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Interfaces;

/// <summary>
/// Optional asynchronous staging contract used by high-volume ingestion.
/// </summary>
public interface IAsyncIngestionStagingRepository
{
    Task SaveValidFindingsAsync(
        string jobId,
        IReadOnlyList<FileFinding> validFindings,
        CancellationToken cancellationToken = default);

    Task DeleteByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken = default);
}
