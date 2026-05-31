using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;
using RemediationTool.Domain.Enums;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// JSON file-backed implementation of IFileFindingRepository.
/// Used during local development only (Persistence:Provider = Json in appsettings.json).
/// Do NOT use in production — switch to DynamoDB when AWS connectivity is available.
/// </summary>
public sealed class JsonFileFindingRepository : IFileFindingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonFileFindingRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "metadata.json");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "[]");
    }

    // =========================================================================
    // WRITE OPERATIONS
    // =========================================================================

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0) return;

        lock (_lock)
        {
            var all = ReadAllInternal();
            all.AddRange(findings);
            WriteAllInternal(all);
        }
    }

    public void Add(FileFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        lock (_lock)
        {
            var all = ReadAllInternal();
            all.Add(finding);
            WriteAllInternal(all);
        }
    }

    /// <summary>
    /// Mutates an existing record in place (used by POC services).
    /// When QuarantineService/DeleteService/RestoreService are properly implemented
    /// per spec, they should use Add() to insert a new version row instead.
    /// </summary>
    public void Update(FileFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        lock (_lock)
        {
            var all = ReadAllInternal();
            var index = all.FindIndex(x => x.Id == finding.Id);

            if (index >= 0)
                all[index] = finding;
            else
                all.Add(finding);

            WriteAllInternal(all);
        }
    }

    // =========================================================================
    // SINGLE-RECORD LOOKUPS
    // =========================================================================

    public FileFinding? GetById(Guid id)
    {
        lock (_lock)
        {
            return ReadAllInternal().FirstOrDefault(x => x.Id == id);
        }
    }

    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(sourceRecordId)) return null;

        lock (_lock)
        {
            return ReadAllInternal()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastUpdateDateUtc)
                .FirstOrDefault();
        }
    }

    // =========================================================================
    // FILTERED QUERIES
    // =========================================================================

    public IReadOnlyList<FileFinding> GetLatestByFindingType(FindingType findingType)
    {
        lock (_lock)
        {
            return ReadAllInternal()
                .GroupBy(GetGroupKey)
                .Select(g => g.OrderByDescending(x => x.LastUpdateDateUtc).First())
                .Where(x => x.FindingType == findingType)
                .OrderBy(x => x.FindingFileName)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        if (string.IsNullOrWhiteSpace(dataSystem)) return Array.Empty<FileFinding>();

        lock (_lock)
        {
            return ReadAllInternal()
                .GroupBy(GetGroupKey)
                .Select(g => g.OrderByDescending(x => x.LastUpdateDateUtc).First())
                .Where(x => string.Equals(x.DataSystem, dataSystem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FindingFileName)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(sourceRecordId)) return Array.Empty<FileFinding>();

        lock (_lock)
        {
            return ReadAllInternal()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LastUpdateDateUtc)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        if (string.IsNullOrWhiteSpace(ingestionJobId)) return Array.Empty<FileFinding>();

        lock (_lock)
        {
            return ReadAllInternal()
                .Where(x => string.Equals(x.IngestionJobId, ingestionJobId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LoadDateUtc)
                .ToList();
        }
    }

    // =========================================================================
    // PAGED QUERY
    // =========================================================================

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize, string? lastEvaluatedKey = null, FindingType? findingType = null)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");

        lock (_lock)
        {
            var query = ReadAllInternal()
                .GroupBy(GetGroupKey)
                .Select(g => g.OrderByDescending(x => x.LastUpdateDateUtc).First())
                .OrderBy(x => x.FindingFileName)
                .ThenBy(x => x.Id)
                .AsEnumerable();

            if (findingType.HasValue)
                query = query.Where(x => x.FindingType == findingType.Value);

            var allLatest = query.ToList();

            var skipCount = 0;
            if (!string.IsNullOrWhiteSpace(lastEvaluatedKey) && int.TryParse(lastEvaluatedKey, out var parsed))
                skipCount = parsed;

            var page = allLatest.Skip(skipCount).Take(pageSize).ToList();
            var nextSkip = skipCount + page.Count;
            var nextKey = nextSkip < allLatest.Count ? nextSkip.ToString() : null;

            return new PagedResult<FileFinding> { Items = page, NextPageKey = nextKey };
        }
    }

    // =========================================================================
    // AGGREGATE / COUNT QUERIES
    // =========================================================================

    public IReadOnlyDictionary<FindingType, int> GetCountByFindingType()
    {
        lock (_lock)
        {
            return ReadAllInternal()
                .GroupBy(GetGroupKey)
                .Select(g => g.OrderByDescending(x => x.LastUpdateDateUtc).First())
                .GroupBy(x => x.FindingType)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public int CountByFindingType(FindingType findingType)
    {
        lock (_lock)
        {
            return ReadAllInternal()
                .GroupBy(GetGroupKey)
                .Select(g => g.OrderByDescending(x => x.LastUpdateDateUtc).First())
                .Count(x => x.FindingType == findingType);
        }
    }

    // =========================================================================
    // INTERNAL HELPERS
    // =========================================================================

    private static string GetGroupKey(FileFinding f)
        => string.IsNullOrWhiteSpace(f.SourceRecordId) ? f.Id.ToString() : f.SourceRecordId;

    private List<FileFinding> ReadAllInternal()
    {
        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
            return new List<FileFinding>();

        return JsonSerializer.Deserialize<List<FileFinding>>(json, JsonOptions)
               ?? new List<FileFinding>();
    }

    private void WriteAllInternal(List<FileFinding> findings)
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(findings, JsonOptions));
    }
}