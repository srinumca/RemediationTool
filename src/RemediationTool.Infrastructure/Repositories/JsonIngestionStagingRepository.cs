using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionStagingRepository : IIngestionStagingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonIngestionStagingRepository()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "ingestion-staged-findings.json");

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
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
            stagedFindings.RemoveAll(existing =>
                string.Equals(existing.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            stagedFindings.EnsureCapacity(stagedFindings.Count + validFindings.Count);
            var createdAtUtc = DateTime.UtcNow;

            for (var index = 0; index < validFindings.Count; index++)
            {
                stagedFindings.Add(new IngestionStagedFinding
                {
                    JobId = jobId,
                    SequenceNumber = index + 1,
                    Finding = validFindings[index],
                    CreatedAtUtc = createdAtUtc
                });
            }

            SaveAll(stagedFindings);
        }
    }

    public List<FileFinding> GetValidFindingsAfter(
        string jobId,
        int lastProcessedRecordCount)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return new List<FileFinding>();

        lock (_lock)
        {
            var stagedFindings = LoadAll();
            var result = new List<FileFinding>();

            foreach (var record in stagedFindings)
            {
                if (record.SequenceNumber > lastProcessedRecordCount
                    && string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(record.Finding);
                }
            }

            return result;
        }
    }

    public int CountByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return 0;

        lock (_lock)
        {
            var count = 0;
            foreach (var record in LoadAll())
            {
                if (string.Equals(record.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }
    }

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

        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionStagedFinding>();

        return JsonSerializer.Deserialize<List<IngestionStagedFinding>>(json, JsonOptions)
               ?? new List<IngestionStagedFinding>();
    }

    private void SaveAll(List<IngestionStagedFinding> stagedFindings)
    {
        var json = JsonSerializer.Serialize(stagedFindings, JsonOptions);
        JsonFileHelper.WriteAllText(_filePath, json);
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
