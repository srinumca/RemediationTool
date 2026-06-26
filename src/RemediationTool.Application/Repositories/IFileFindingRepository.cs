using RemediationTool.Domain.Entities;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Repository contract for FileFinding records.
/// FindingType is string throughout — no enum dependency.
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

    // ── Legacy — used by ReportService, QuarantineService, DeleteService ──────
    List<FileFinding> GetAll();
}