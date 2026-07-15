using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Optional asynchronous contract for high-volume finding writes.
/// Existing synchronous repository contracts remain unchanged for compatibility.
/// </summary>
public interface IAsyncFileFindingRepository
{
    Task AddRangeAsync(
        IReadOnlyList<FileFinding> findings,
        CancellationToken cancellationToken = default);
}
