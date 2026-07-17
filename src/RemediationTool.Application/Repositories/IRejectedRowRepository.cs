using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Persistence contract required by ingestion rejected-row writes.
/// </summary>
public interface IRejectedRowRepository
{
    void AddRange(List<RejectedRowDetail> rejectedRows);
}
