using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using RemediationTool.Application.Repositories;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

/// <summary>
/// JSON file-backed implementation of IFileFindingRepository.
/// Used for local development only.
/// </summary>
public sealed class JsonFileFindingRepository : IFileFindingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonFileFindingRepository(IConfiguration configuration)
    {
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "metadata.json");

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
    }

    public void Add(FileFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        lock (_lock)
        {
            var all = ReadAll();
            all.Add(finding);
            WriteAll(all);
        }
    }

    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
            return;

        lock (_lock)
        {
            var all = ReadAll();
            all.EnsureCapacity(all.Count + findings.Count);

            foreach (var finding in findings)
                all.Add(finding);

            WriteAll(all);
        }
    }

    public void Update(FileFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        lock (_lock)
        {
            var all = ReadAll();
            var index = all.FindIndex(existing => existing.Id == finding.Id);

            if (index >= 0)
                all[index] = finding;
            else
                all.Add(finding);

            WriteAll(all);
        }
    }

    public FileFinding? GetById(Guid id)
    {
        lock (_lock)
        {
            return ReadAll().Find(finding => finding.Id == id);
        }
    }

    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(sourceRecordId))
            return null;

        lock (_lock)
        {
            FileFinding? latest = null;

            foreach (var finding in ReadAll())
            {
                if (!string.Equals(
                        finding.SourceRecordId,
                        sourceRecordId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (latest == null || finding.LastUpdateDateUtc > latest.LastUpdateDateUtc)
                    latest = finding;
            }

            return latest;
        }
    }

    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        lock (_lock)
        {
            var result = Filter(
                ReadAll(),
                finding => string.Equals(
                    finding.IngestionJobId,
                    ingestionJobId,
                    StringComparison.OrdinalIgnoreCase));

            result.Sort(static (left, right) => left.LoadDateUtc.CompareTo(right.LoadDateUtc));
            return result;
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
    {
        lock (_lock)
        {
            var result = Filter(
                ReadAll(),
                finding => string.Equals(
                    finding.FindingType,
                    findingType,
                    StringComparison.OrdinalIgnoreCase));

            result.Sort(static (left, right) =>
                string.Compare(
                    left.FindingFileName,
                    right.FindingFileName,
                    StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }

    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        lock (_lock)
        {
            var result = Filter(
                ReadAll(),
                finding => string.Equals(
                    finding.OriginatingDataSystem,
                    dataSystem,
                    StringComparison.OrdinalIgnoreCase));

            result.Sort(static (left, right) =>
                string.Compare(
                    left.FindingFileName,
                    right.FindingFileName,
                    StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }

    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        lock (_lock)
        {
            var result = Filter(
                ReadAll(),
                finding => string.Equals(
                    finding.SourceRecordId,
                    sourceRecordId,
                    StringComparison.OrdinalIgnoreCase));

            result.Sort(static (left, right) =>
                left.LastUpdateDateUtc.CompareTo(right.LastUpdateDateUtc));
            return result;
        }
    }

    public List<FileFinding> GetAll()
    {
        lock (_lock)
        {
            return ReadAll();
        }
    }

    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize,
        string? lastEvaluatedKey = null,
        string? findingType = null)
    {
        lock (_lock)
        {
            var all = string.IsNullOrWhiteSpace(findingType)
                ? ReadAll()
                : Filter(
                    ReadAll(),
                    finding => string.Equals(
                        finding.FindingType,
                        findingType,
                        StringComparison.OrdinalIgnoreCase));

            all.Sort(static (left, right) =>
                string.Compare(
                    left.FindingFileName,
                    right.FindingFileName,
                    StringComparison.OrdinalIgnoreCase));

            var skip = int.TryParse(lastEvaluatedKey, out var parsedSkip)
                ? Math.Max(0, parsedSkip)
                : 0;
            var take = Math.Max(0, pageSize);

            if (skip >= all.Count || take == 0)
            {
                return new PagedResult<FileFinding>
                {
                    Items = new List<FileFinding>(),
                    NextPageKey = null
                };
            }

            var pageCount = Math.Min(take, all.Count - skip);
            var page = all.GetRange(skip, pageCount);
            var nextOffset = skip + pageCount;

            return new PagedResult<FileFinding>
            {
                Items = page,
                NextPageKey = nextOffset < all.Count ? nextOffset.ToString() : null
            };
        }
    }

    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        lock (_lock)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in ReadAll())
            {
                var findingType = finding.FindingType ?? string.Empty;
                if (counts.TryGetValue(findingType, out var currentCount))
                    counts[findingType] = currentCount + 1;
                else
                    counts[findingType] = 1;
            }

            return counts;
        }
    }

    public int CountByFindingType(string findingType)
    {
        lock (_lock)
        {
            var count = 0;
            foreach (var finding in ReadAll())
            {
                if (string.Equals(
                        finding.FindingType,
                        findingType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }
    }

    private List<FileFinding> ReadAll()
    {
        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<FileFinding>();

        var trimmed = json.AsSpan().Trim();
        if (trimmed.SequenceEqual("[]".AsSpan()))
            return new List<FileFinding>();

        return JsonSerializer.Deserialize<List<FileFinding>>(json, JsonOptions)
               ?? new List<FileFinding>();
    }

    private void WriteAll(List<FileFinding> findings)
        => JsonFileHelper.WriteAllText(
            _filePath,
            JsonSerializer.Serialize(findings, JsonOptions));

    private static List<FileFinding> Filter(
        List<FileFinding> findings,
        Func<FileFinding, bool> predicate)
    {
        var result = new List<FileFinding>();

        foreach (var finding in findings)
        {
            if (predicate(finding))
                result.Add(finding);
        }

        return result;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
