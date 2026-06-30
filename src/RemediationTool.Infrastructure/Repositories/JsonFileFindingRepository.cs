using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<JsonFileFindingRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonFileFindingRepository(IConfiguration configuration, ILogger<JsonFileFindingRepository> logger)
    {
        _logger = logger;
        var rootPath = configuration["Persistence:JsonRootPath"] ?? "storage";
        _filePath = Path.Combine(rootPath, "metadata.json");

        _logger.LogInformation("JsonFileFindingRepository initialized with FilePath: {FilePath}", _filePath);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(_filePath)) File.WriteAllText(_filePath, "[]");
    }

    /// <summary>
    /// Adds a new FileFinding record to the JSON repository.
    /// </summary>
    /// <param name="finding"></param>
    public void Add(FileFinding finding)
    {
        _logger.LogDebug("Adding FileFinding. Id: {Id}, FileName: {FileName}", finding.Id, finding.FindingFileName);
        lock (_lock)
        {
            var all = ReadAll();
            all.Add(finding);
            WriteAll(all);
            _logger.LogDebug("FileFinding added successfully. Id: {Id}, TotalRecordsNow: {Total}", finding.Id, all.Count);
        }
    }

    /// <summary>
    /// Adds a range of FileFinding records to the JSON repository.
    /// </summary>
    /// <param name="findings"></param>
    public void AddRange(IReadOnlyList<FileFinding> findings)
    {
        if (findings == null || findings.Count == 0)
        {
            _logger.LogDebug("AddRange called with empty findings list");
            return;
        }

        _logger.LogInformation("Adding {Count} FileFinding records", findings.Count);
        lock (_lock)
        {
            var all = ReadAll();
            all.AddRange(findings);
            WriteAll(all);
            _logger.LogInformation("Successfully added {Count} FileFinding records. TotalRecordsNow: {Total}", findings.Count, all.Count);
        }
    }

    /// <summary>
    /// Updates an existing FileFinding record in the JSON repository.
    /// </summary>
    /// <param name="finding"></param>
    public void Update(FileFinding finding)
    {
        _logger.LogDebug("Updating FileFinding. Id: {Id}, FileName: {FileName}", finding.Id, finding.FindingFileName);
        lock (_lock)
        {
            var all = ReadAll();
            var index = all.FindIndex(x => x.Id == finding.Id);
            if (index >= 0)
            {
                all[index] = finding;
                _logger.LogDebug("FileFinding updated at index {Index}", index);
            }
            else
            {
                all.Add(finding);
                _logger.LogDebug("FileFinding not found for update, adding as new. Id: {Id}", finding.Id);
            }
            WriteAll(all);
        }
    }

    /// <summary>
    /// Retrieves a FileFinding record by its unique Id.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public FileFinding? GetById(Guid id)
    {
        _logger.LogDebug("Getting FileFinding by Id: {Id}", id);
        lock (_lock)
        {
            var result = ReadAll().FirstOrDefault(x => x.Id == id);
            if (result != null)
                _logger.LogDebug("FileFinding found. Id: {Id}, FileName: {FileName}", id, result.FindingFileName);
            else
                _logger.LogDebug("FileFinding not found. Id: {Id}", id);
            return result;
        }
    }

    /// <summary>
    /// Retrieves the latest FileFinding record by SourceRecordId, ordered by LastUpdateDateUtc descending.
    /// </summary>
    /// <param name="sourceRecordId"></param>
    /// <returns></returns>
    public FileFinding? GetLatestBySourceRecordId(string sourceRecordId)
    {
        _logger.LogDebug("Getting latest FileFinding by SourceRecordId: {SourceRecordId}", sourceRecordId);
        lock (_lock)
        {
            var result = ReadAll()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.LastUpdateDateUtc)
                .FirstOrDefault();
            if (result != null)
                _logger.LogDebug("FileFinding found. SourceRecordId: {SourceRecordId}, FileName: {FileName}", sourceRecordId, result.FindingFileName);
            else
                _logger.LogDebug("No FileFinding found. SourceRecordId: {SourceRecordId}", sourceRecordId);
            return result;
        }
    }

    /// <summary>
    /// Retrieves all FileFinding records associated with a specific IngestionJobId, ordered by LoadDateUtc ascending.
    /// </summary>
    /// <param name="ingestionJobId"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetByIngestionJobId(string ingestionJobId)
    {
        _logger.LogDebug("Getting FileFinding records by IngestionJobId: {IngestionJobId}", ingestionJobId);
        lock (_lock)
        {
            var results = ReadAll()
                .Where(x => string.Equals(x.IngestionJobId, ingestionJobId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LoadDateUtc)
                .ToList();
            _logger.LogInformation("Retrieved {Count} FileFinding records for IngestionJobId: {IngestionJobId}", results.Count, ingestionJobId);
            return results;
        }
    }

    /// <summary>
    /// Retrieves all FileFinding records associated with a specific FindingType, ordered by FindingFileName ascending.
    /// </summary>
    /// <param name="findingType"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetLatestByFindingType(string findingType)
    {
        _logger.LogDebug("Getting FileFinding records by FindingType: {FindingType}", findingType);
        lock (_lock)
        {
            var results = ReadAll()
                .Where(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FindingFileName)
                .ToList();
            _logger.LogInformation("Retrieved {Count} FileFinding records for FindingType: {FindingType}", results.Count, findingType);
            return results;
        }
    }

    /// <summary>
    /// Retrieves all FileFinding records associated with a specific OriginatingDataSystem, ordered by FindingFileName ascending.
    /// </summary>
    /// <param name="dataSystem"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetLatestByDataSystem(string dataSystem)
    {
        _logger.LogDebug("Getting FileFinding records by DataSystem: {DataSystem}", dataSystem);
        lock (_lock)
        {
            var results = ReadAll()
                .Where(x => string.Equals(x.OriginatingDataSystem, dataSystem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FindingFileName)
                .ToList();
            _logger.LogInformation("Retrieved {Count} FileFinding records for DataSystem: {DataSystem}", results.Count, dataSystem);
            return results;
        }
    }

    /// <summary>
    /// Retrieves the history of FileFinding records associated with a specific SourceRecordId, ordered by LastUpdateDateUtc ascending.
    /// </summary>
    /// <param name="sourceRecordId"></param>
    /// <returns></returns>
    public IReadOnlyList<FileFinding> GetHistoryBySourceRecordId(string sourceRecordId)
    {
        _logger.LogDebug("Getting FileFinding history by SourceRecordId: {SourceRecordId}", sourceRecordId);
        lock (_lock)
        {
            var results = ReadAll()
                .Where(x => string.Equals(x.SourceRecordId, sourceRecordId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.LastUpdateDateUtc)
                .ToList();
            _logger.LogInformation("Retrieved {Count} historical records for SourceRecordId: {SourceRecordId}", results.Count, sourceRecordId);
            return results;
        }
    }

    /// <summary>
    /// Retrieves all FileFinding records in the repository, ordered by FindingFileName ascending.
    /// </summary>
    /// <returns></returns>
    public List<FileFinding> GetAll()
    {
        _logger.LogDebug("Getting all FileFinding records");
        lock (_lock)
        {
            var results = ReadAll();
            _logger.LogDebug("Retrieved {Count} total FileFinding records", results.Count);
            return results;
        }
    }

    /// <summary>
    /// Retrieves a paged list of FileFinding records, optionally filtered by FindingType, ordered by FindingFileName ascending.
    /// </summary>
    /// <param name="pageSize"></param>
    /// <param name="lastEvaluatedKey"></param>
    /// <param name="findingType"></param>
    /// <returns></returns>
    public PagedResult<FileFinding> GetLatestPaged(
        int pageSize, string? lastEvaluatedKey = null, string? findingType = null)
    {
        _logger.LogDebug("Getting paged FileFinding records. PageSize: {PageSize}, FindingType: {FindingType}, LastEvaluatedKey: {LastEvaluatedKey}", 
            pageSize, findingType, lastEvaluatedKey);
        lock (_lock)
        {
            var query = ReadAll().AsEnumerable();
            if (!string.IsNullOrWhiteSpace(findingType))
                query = query.Where(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase));

            var all = query.OrderBy(x => x.FindingFileName).ToList();
            var skip = int.TryParse(lastEvaluatedKey, out var s) ? s : 0;
            var page = all.Skip(skip).Take(pageSize).ToList();
            var nextKey = skip + page.Count < all.Count ? (skip + page.Count).ToString() : null;

            _logger.LogInformation("Paged query returned {PageRecordCount} records from offset {Offset} of {TotalRecords}", page.Count, skip, all.Count);

            return new PagedResult<FileFinding> { Items = page, NextPageKey = nextKey };
        }
    }

    /// <summary>
    /// Retrieves a count of FileFinding records grouped by FindingType.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyDictionary<string, int> GetCountByFindingType()
    {
        _logger.LogDebug("Getting count aggregation by FindingType");
        lock (_lock)
        {
            var result = ReadAll()
                .GroupBy(x => x.FindingType)
                .ToDictionary(g => g.Key, g => g.Count());
            _logger.LogInformation("FindingType count aggregation complete. GroupCount: {GroupCount}", result.Count);
            return result;
        }
    }

    /// <summary>
    /// Retrieves a count of FileFinding records for a specific FindingType.
    /// </summary>
    /// <param name="findingType"></param>
    /// <returns></returns>
    public int CountByFindingType(string findingType)
    {
        _logger.LogDebug("Counting FileFinding records by FindingType: {FindingType}", findingType);
        lock (_lock)
        {
            var count = ReadAll()
                .Count(x => string.Equals(x.FindingType, findingType, StringComparison.OrdinalIgnoreCase));
            _logger.LogInformation("Count for FindingType {FindingType}: {Count}", findingType, count);
            return count;
        }
    }

    /// <summary>
    /// Reads all FileFinding records from the JSON file.
    /// </summary>
    /// <returns></returns>
    private List<FileFinding> ReadAll()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                return new List<FileFinding>();
            return JsonSerializer.Deserialize<List<FileFinding>>(json, JsonOptions)
                   ?? new List<FileFinding>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading JSON repository from {FilePath}", _filePath);
            throw;
        }
    }

    /// <summary>
    /// Writes all FileFinding records to the JSON file.
    /// </summary>
    /// <param name="findings"></param>
    private void WriteAll(List<FileFinding> findings)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(findings, JsonOptions));
            _logger.LogDebug("JSON repository written successfully with {Count} records", findings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing JSON repository to {FilePath}", _filePath);
            throw;
        }
    }
}