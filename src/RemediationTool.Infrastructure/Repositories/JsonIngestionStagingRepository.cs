using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionStagingRepository : IIngestionStagingRepository
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonIngestionStagingRepository()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "ingestion-staged-findings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public void SaveValidFindings(string jobId, List<FileFinding> validFindings)
    {
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

    private void SaveAll(List<IngestionStagedFinding> stagedFindings)
    {
        var json = JsonSerializer.Serialize(stagedFindings, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}