using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Optional asynchronous contract for bounded rejected-row persistence.
/// Existing synchronous repository contracts remain unchanged for compatibility.
/// </summary>
public interface IAsyncRejectedRowRepository
{
    Task AddRangeAsync(
        IReadOnlyList<RejectedRowDetail> rejectedRows,
        CancellationToken cancellationToken = default);
}
