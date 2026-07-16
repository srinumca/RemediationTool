using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Persistence contract required by ingestion writes and dashboard job views.
/// </summary>
public interface IFileFindingRepository
{
    void AddRange(IReadOnlyList<FileFinding> findings);

    IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId);
}
