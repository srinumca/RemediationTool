using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionCheckpointRepository : IIngestionCheckpointRepository
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonIngestionCheckpointRepository()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "ingestion-checkpoints.json");

        if (!File.Exists(_filePath))
            JsonFileHelper.WriteAllText(_filePath, "[]");
    }

    public IngestionCheckpoint? GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        lock (_lock)
        {
            return LoadAll().Find(checkpoint =>
                string.Equals(checkpoint.JobId, jobId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Upsert(IngestionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        lock (_lock)
        {
            var checkpoints = LoadAll();
            var existingIndex = checkpoints.FindIndex(existing =>
                string.Equals(existing.JobId, checkpoint.JobId, StringComparison.OrdinalIgnoreCase));

            var nowUtc = DateTime.UtcNow;
            checkpoint.LastCheckpointUtc = nowUtc;

            if (existingIndex >= 0)
            {
                checkpoints[existingIndex] = checkpoint;
            }
            else
            {
                checkpoint.CreatedAtUtc = nowUtc;
                checkpoints.Add(checkpoint);
            }

            SaveAll(checkpoints);
        }
    }

    private List<IngestionCheckpoint> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<IngestionCheckpoint>();

        var json = JsonFileHelper.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionCheckpoint>();

        return JsonSerializer.Deserialize<List<IngestionCheckpoint>>(json, JsonOptions)
               ?? new List<IngestionCheckpoint>();
    }

    private void SaveAll(List<IngestionCheckpoint> checkpoints)
    {
        var json = JsonSerializer.Serialize(checkpoints, JsonOptions);
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
