using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<JsonIngestionStagingRepository> _logger;

    public JsonIngestionStagingRepository(ILogger<JsonIngestionStagingRepository> logger)
    {
        _logger = logger;
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "ingestion-staged-findings.json");

        _logger.LogInformation("JsonIngestionStagingRepository initialized with FilePath: {FilePath}", _filePath);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Saves valid findings for a given JobId. If there are existing staged records for the same JobId, they will be replaced (idempotent on re-upload).
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="validFindings"></param>
    /// <exception cref="ArgumentException"></exception>
    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
        _logger.LogInformation("Saving {Count} valid findings for JobId: {JobId}", validFindings?.Count ?? 0, jobId);
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("JobId is required.", nameof(jobId));

        if (validFindings == null || validFindings.Count == 0)
            return;

        lock (_lock)
        {
            var stagedFindings = LoadAll();

            // Replace any existing staged records for this jobId (idempotent on re-upload)
            stagedFindings.RemoveAll(existing =>
                string.Equals(existing.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            var newRecords = validFindings
                .Select((finding, index) => new IngestionStagedFinding
                {
                    JobId = jobId,
                    SequenceNumber = index + 1,
                    Finding = finding,
                    CreatedAtUtc = DateTime.UtcNow
                })
                .ToList();

            stagedFindings.AddRange(newRecords);
            SaveAll(stagedFindings);
        }
    }

    /// <summary>
    /// Retrieves valid findings for a given JobId that have a SequenceNumber greater than the provided lastProcessedRecordCount. This allows for incremental processing of staged findings.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="lastProcessedRecordCount"></param>
    /// <returns></returns>
    public List<FileFinding> GetValidFindingsAfter(string jobId, int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<FileFinding>();

        lock (_lock)
        {
            return LoadAll()
                .Where(record =>
                    string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase)
                    && record.SequenceNumber > lastProcessedRecordCount)
                .OrderBy(record => record.SequenceNumber)
                .Select(record => record.Finding)
                .ToList();
        }
    }

    /// <summary>
    /// Counts the number of staged records for a given JobId. This can be used to determine how many records are available for processing.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    public int CountByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return 0;

        lock (_lock)
        {
            return LoadAll()
                .Count(record =>
                    string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Removes all staged records for the given JobId.
    /// Called after successful job completion to prevent unbounded file growth.
    /// </summary>
    public void DeleteByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        lock (_lock)
        {
            var stagedFindings = LoadAll();
            var removed = stagedFindings.RemoveAll(record =>
                string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
                SaveAll(stagedFindings);
        }
    }

    /// <summary>
    /// Loads all staged findings from the JSON file. If the file does not exist or is empty, returns an empty list.
    /// </summary>
    /// <returns></returns>
    private List<IngestionStagedFinding> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<IngestionStagedFinding>();

        var json = File.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionStagedFinding>();

        return JsonSerializer.Deserialize<List<IngestionStagedFinding>>(json, _jsonOptions)
               ?? new List<IngestionStagedFinding>();
    }

    /// <summary>
    /// Saves all staged findings to the JSON file, overwriting any existing content. This is called after adding or removing records to ensure the file reflects the current state.
    /// </summary>
    /// <param name="stagedFindings"></param>
    private void SaveAll(List<IngestionStagedFinding> stagedFindings)
    {
        var json = JsonSerializer.Serialize(stagedFindings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}