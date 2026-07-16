using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Optional asynchronous contract for high-volume ingestion finding writes.
/// </summary>
public interface IAsyncFileFindingRepository
{
    Task AddRangeAsync(
        IReadOnlyList<FileFinding> findings,
        CancellationToken cancellationToken = default);
}
