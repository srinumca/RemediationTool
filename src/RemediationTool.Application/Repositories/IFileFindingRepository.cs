using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Application.Repositories;

/// <summary>
/// Repository contract for persisting and querying FileFinding records.
///
/// No GetAll() — loading the full dataset is not viable at scale (millions of files).
/// All query methods accept explicit filters to enable efficient DynamoDB index usage.
/// Paging uses a cursor model (lastEvaluatedKey) compatible with DynamoDB pagination.
///
/// Update() is provided for the POC quarantine/delete/restore services.
/// Per the spec's append-only pattern, when these services are properly implemented
/// they should use Add() to insert a new version row rather than mutating in place.
/// </summary>
public interface IFileFindingRepository
{
    // =========================================================================
    // WRITE OPERATIONS
    // =========================================================================

    /// <summary>Persists a batch of new FileFinding records (primary ingestion write path).</summary>
    void AddRange(IReadOnlyList<FileFinding> findings);

    /// <summary>
    /// Inserts a new version row for a single FileFinding record.
    /// Follows the append-only pattern — new row inserted, existing row not mutated.
    /// </summary>
    void Add(FileFinding finding);

    /// <summary>
    /// Updates an existing record in place (mutates the existing row).
    /// Used by the POC QuarantineService, DeleteService, and RestoreService.
    /// NOTE: When those services are properly implemented per spec, they should
    /// use Add() to insert a new version row instead (append-only pattern).
    /// </summary>
    void Update(FileFinding finding);

    // =========================================================================
    // SINGLE-RECORD LOOKUPS
    // =========================================================================

    /// <summary>Returns a specific record version by its internal system Id. Null if not found.</summary>
    FileFinding? GetById(Guid id);

    /// <summary>
    /// Returns the most recent record version for a given SourceRecordId
    /// (latest by LastUpdateDateUtc). Null if no records exist.
    /// </summary>
    FileFinding? GetLatestBySourceRecordId(string sourceRecordId);

    // =========================================================================
    // FILTERED QUERIES
    // =========================================================================

    /// <summary>
    /// Returns the most recent record version per finding currently at the specified FindingType.
    /// Used by: AutomatedQuarantine (Obsolete), AutomatedDeletion (Quarantined), reporting.
    /// </summary>
    IReadOnlyList<FileFinding> GetLatestByFindingType(FindingType findingType);

    /// <summary>
    /// Returns the most recent record version per finding for the specified DataSystem.
    /// Used by: reporting breakdowns by data system.
    /// </summary>
    IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem);

    /// <summary>
    /// Returns all record versions (full history) for a given SourceRecordId, oldest to newest.
    /// Used by: audit trail drill-through views.
    /// </summary>
    IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId);

    /// <summary>
    /// Returns all records loaded under a specific ingestion job.
    /// Used by: ingestion audit reports.
    /// </summary>
    IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId);

    // =========================================================================
    // PAGED QUERY
    // =========================================================================

    /// <summary>
    /// Returns a page of the most recent record version per finding, optionally filtered by FindingType.
    /// Pass null/empty lastEvaluatedKey to start from the beginning.
    /// NextPageKey in the result is null when no more pages exist.
    /// </summary>
    PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        FindingType? findingType = null);

    // =========================================================================
    // AGGREGATE / COUNT QUERIES
    // =========================================================================

    /// <summary>Returns count of most recent records per finding grouped by FindingType. Used by dashboard KPI cards.</summary>
    IReadOnlyDictionary<FindingType, int> GetCountByFindingType();

    /// <summary>Returns count of most recent records for a single FindingType. Used by targeted KPI cards.</summary>
    int CountByFindingType(FindingType findingType);
}