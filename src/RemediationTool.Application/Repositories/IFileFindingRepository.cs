using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enum;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Repository contract for FileFinding records.
/// Matches the actual DynamoDbFileFindingRepository implementation.
/// FindingType is stored and queried as a string — no enum required at the interface level.
/// </summary>
public interface IFileFindingRepository
{
    // ── Write ────────────────────────────────────────────────────────────────
    void Add(FileFinding finding);
    void AddRange(IReadOnlyList<FileFinding> findings);
    void Update(FileFinding finding);

    // ── Single-record lookups ─────────────────────────────────────────────────
    FileFinding? GetById(Guid id);
    FileFinding? GetLatestBySourceRecordId(string sourceRecordId);

    // ── Filtered queries ──────────────────────────────────────────────────────
    IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId);
    IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType);
    IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem);
    IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId);

    // ── Paged query ───────────────────────────────────────────────────────────
    PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        string? findingType = null);

    // ── Aggregates ────────────────────────────────────────────────────────────
    IReadOnlyDictionary<string, int> GetCountByFindingType();
    int CountByFindingType(string findingType);

    // ── Legacy compat (ReportService still uses this) ─────────────────────────
    List<FileFinding> GetAll();
}