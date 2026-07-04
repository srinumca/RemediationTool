using System.Text.Json;
using System.Text.Json.Serialization;
using RemediationTool.Application.Interfaces;
using RemediationTool.Domain.Entities;

namespace RemediationTool.Infrastructure.Repositories;

public class JsonIngestionCheckpointRepository : IIngestionCheckpointRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonIngestionCheckpointRepository()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);

        _filePath = Path.Combine(dataDirectory, "ingestion-checkpoints.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public IngestionCheckpoint? GetByJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return null;

        var checkpoints = LoadAll();

        return checkpoints.FirstOrDefault(checkpoint =>
            string.Equals(checkpoint.JobId, jobId, StringComparison.OrdinalIgnoreCase));
    }

    public void Upsert(IngestionCheckpoint checkpoint)
    {
        if (checkpoint == null)
            throw new ArgumentNullException(nameof(checkpoint));

        var checkpoints = LoadAll();

        var existingIndex = checkpoints.FindIndex(existing =>
            string.Equals(existing.JobId, checkpoint.JobId, StringComparison.OrdinalIgnoreCase));

        checkpoint.LastCheckpointUtc = DateTime.UtcNow;

        if (existingIndex >= 0)
        {
            checkpoints[existingIndex] = checkpoint;
        }
        else
        {
            checkpoint.CreatedAtUtc = DateTime.UtcNow;
            checkpoints.Add(checkpoint);
        }

        SaveAll(checkpoints);
    }

    private List<IngestionCheckpoint> LoadAll()
    {
        if (!File.Exists(_filePath))
            return new List<IngestionCheckpoint>();

        var json = File.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new List<IngestionCheckpoint>();

        return JsonSerializer.Deserialize<List<IngestionCheckpoint>>(json, _jsonOptions)
               ?? new List<IngestionCheckpoint>();
    }

    private void SaveAll(List<IngestionCheckpoint> checkpoints)
    {
        var json = JsonSerializer.Serialize(checkpoints, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }
}