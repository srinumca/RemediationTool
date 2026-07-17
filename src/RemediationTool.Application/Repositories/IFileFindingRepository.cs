using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Persistence contract required by ingestion finding writes.
/// </summary>
public interface IFileFindingRepository
{
    void AddRange(IReadOnlyList<FileFinding> findings);
}
