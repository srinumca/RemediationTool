using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// JSON file-backed implementation of IFileFindingRepository.
/// Used for local development only (Persistence:Provider = Json).
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

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public void Add(FileFinding finding)
    {
        lock (_lock)
        {
            var all = ReadAll();
            all.Add(finding);
            WriteAll(all);
        }
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0) return;
        lock (_lock)
        {
            var all = ReadAll();
            all.AddRange(findings);
            WriteAll(all);
        }
    }

    public void Update(FileFinding finding)
    {
        lock (_lock)
        {
            var all = ReadAll();
            var index = all.FindIndex(x => x.Id == finding.Id);
            if (index >= 0) all[index] = finding; else all.Add(finding);
            WriteAll(all);
        }
    }

    // ── Single-record lookups ─────────────────────────────────────────────────

    public FileFinding? GetById(Guid id)
    {
        lock (_lock) { return ReadAll().FirstOrDefault(x => x.Id == id); }
    }

    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        lock (_lock)
        {
            return ReadAll()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastUpdateDateUtc)
                .FirstOrDefault();
        }
    }

    // ── Filtered queries ──────────────────────────────────────────────────────

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        lock (_lock)
        {
            return ReadAll()
                .Where(x => string.Equals(x.IngestionJobId, ingestionJobId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LoadDateUtc)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
    {
        lock (_lock)
        {
            return ReadAll()
                .Where(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FindingFileName)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        lock (_lock)
        {
            return ReadAll()
                .Where(x => string.Equals(x.OriginatingDataSystem, dataSystem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FindingFileName)
                .ToList();
        }
    }

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        lock (_lock)
        {
            return ReadAll()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LastUpdateDateUtc)
                .ToList();
        }
    }

    public List<FileFinding> GetAll()
    {
        lock (_lock) { return ReadAll(); }
    }

    // ── Paged query ───────────────────────────────────────────────────────────

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize, string? lastEvaluatedKey = null, string? findingType = null)
    {
        lock (_lock)
        {
            var query = ReadAll().AsEnumerable();
            if (!string.IsNullOrWhiteSpace(findingType))
                query = query.Where(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase));

            var all = query.OrderBy(x => x.FindingFileName).ToList();
            var skip = int.TryParse(lastEvaluatedKey, out var s) ? s : 0;
            var page = all.Skip(skip).Take(pageSize).ToList();
            var nextKey = skip + page.Count < all.Count ? (skip + page.Count).ToString() : null;

            return new PagedResult<FileFinding> { Items = page, NextPageKey = nextKey };
        }
    }

    // ── Aggregates ────────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        lock (_lock)
        {
            return ReadAll()
                .GroupBy(x => x.FindingType)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    public int CountByFindingType(string findingType)
    {
        lock (_lock)
        {
            return ReadAll()
                .Count(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private List<FileFinding> ReadAll()
    {
        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
            return new List<FileFinding>();
        return JsonSerializer.Deserialize<List<FileFinding>>(json, JsonOptions)
               ?? new List<FileFinding>();
    }

    private void WriteAll(List<FileFinding> findings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(findings, JsonOptions));
}